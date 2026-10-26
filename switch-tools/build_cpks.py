#!/usr/bin/env python3
"""Build multiple stock-preserving Switch CPKs using FastCPK, fail closed.

Stock assets and mods stay local to the phone. This tool does NOT implement
ModdingAPI hooks. It publishes output only after a full CPK index and extracted
XFBIN hash verification pass.
"""
import argparse,hashlib,json,os,shutil,subprocess,sys,tempfile,zipfile
from pathlib import Path
from cpk_inventory import inventory,FormatError
from route_nsc import route

TITLE="010084D00CF5E000"

def sha(path):
    h=hashlib.sha256()
    with Path(path).open("rb") as f:
        for b in iter(lambda:f.read(1<<20),b""):h.update(b)
    return h.hexdigest()

def run(cmd,timeout=1800):
    try:
        p=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,
                         text=True,timeout=timeout)
    except subprocess.TimeoutExpired as ex:
        raise FormatError("FastCPK timed out") from ex
    if p.returncode:
        raise FormatError("FastCPK failed: "+" ".join(Path(cmd[0]).parts[-1:])+
                          "\n"+p.stdout[-1800:])

def safe_romfs_target(relative):
    p=relative.replace("\\","/")
    parts=p.split("/")
    if not (p.startswith("data/") and p.endswith(".cpk") and
            all(x not in ("",".","..") for x in parts) and ":" not in p):
        raise FormatError("Target must be a data/.../*.cpk RomFS path")
    return p

def data_root(path):
    root=Path(path)
    if (root/"data").is_dir():
        extras=[x for x in root.iterdir() if x.name!="data"]
        if extras:raise FormatError("Unexpected sibling entries to data/")
        return root/"data"
    # Old FastCPK output variant: direct folders inside output root.
    if not root.is_dir() or not any(root.iterdir()):
        raise FormatError("Extractor produced no files")
    # The caller must preserve data/ prefix; cannot infer a CPK root.
    raise FormatError("FastCPK extraction did not produce data/ directory")

def build(mods,stocks,romfs,extractor,rebuilder,output,*,allow_unported=False,crypto="cc2-storm"):
    if len(stocks)!=len(romfs) or not stocks:
        raise FormatError("One explicit --romfs destination required per --cpk")
    target=Path(output).resolve()
    if target.exists():raise FormatError("Refusing to overwrite existing output directory")
    stocklist=[Path(p).resolve() for p in stocks]
    labels=[safe_romfs_target(x) for x in romfs]
    if len(set(x.casefold() for x in labels))!=len(labels):
        raise FormatError("Duplicate RomFS target path")
    exe=Path(extractor).resolve();pack=Path(rebuilder).resolve()
    if not exe.is_file() or not pack.is_file():
        raise FormatError("Provide existing FastCPK extractor and rebuilder executables")
    scan=route(mods,stocks)
    if scan["missing"] or scan["ambiguous"]:
        raise FormatError("Missing or ambiguous asset routes: no CPKs produced")
    if scan["unported_runtime_dependencies"] and not allow_unported:
        raise FormatError("Windows ModdingAPI/character dependencies remain unported; build stopped")
    if not scan["matched_count"]:
        raise FormatError("None of the selected CPKs contain mod replacements")
    modfiles={}
    for mod in mods:
        path=Path(mod).resolve()
        if path.name in modfiles and modfiles[path.name]!=path:
            raise FormatError("Mods with duplicate file names require unique names")
        modfiles[path.name]=path
    parent=target.parent
    parent.mkdir(parents=True,exist_ok=True)
    for stock in stocklist:
        if stock == target or target in stock.parents:
            raise FormatError("Output directory must not contain or replace source stock CPK")
    results=[]
    with tempfile.TemporaryDirectory(prefix="nsc_switch_multi_",dir=parent) as d:
        tmp=Path(d);ready=tmp/"ready"
        ready.mkdir()
        for k,stock in enumerate(stocklist):
            manifest=scan["replacements"].get(str(stock),[])
            if not manifest:continue
            original_index=inventory(stock)
            extract=tmp/f"input{k}"
            run([str(exe),"--game",crypto,"-o",str(extract),str(stock)])
            root=data_root(extract)
            # Snapshot every untouched extracted file, not only patched assets.
            # A successful repack must preserve the complete original file set
            # and every byte outside the explicitly selected replacements.
            expected_stock_paths={entry["path"].casefold():entry["path"] for entry in original_index["entries"]}
            replacing={entry["virtual_path"].casefold() for entry in manifest}
            baseline_hashes={}
            for path_key,virtual_path in expected_stock_paths.items():
                source=root/virtual_path.removeprefix("data/")
                if not source.is_file() or source.is_symlink():
                    raise FormatError("Stock extractor did not produce indexed file: "+virtual_path)
                if path_key not in replacing:
                    baseline_hashes[path_key]=sha(source)
            extracted_paths={
                "data/"+file.relative_to(root).as_posix().casefold()
                for file in root.rglob("*") if file.is_file()
            }
            if extracted_paths!=set(expected_stock_paths):
                raise FormatError("Extracted stock path set does not match original CPK TOC")
            for entry in manifest:
                virtual=entry["virtual_path"]
                if not virtual.startswith("data/") or ".." in virtual.split("/"):
                    raise FormatError("Refusing non-data replacement")
                final=root/virtual.removeprefix("data/")
                if final.is_symlink() or not final.is_file():
                    raise FormatError("Stock extracted file missing: "+virtual)
                original_mod=modfiles[entry["mod"]]
                with zipfile.ZipFile(original_mod) as z:
                    info=z.getinfo(entry["zip_entry"])
                    with z.open(info) as src,final.open("wb") as dst:
                        shutil.copyfileobj(src,dst,1<<20)
                if sha(final)!=entry["sha256"]:
                    raise FormatError("Copied XFBIN digest mismatch: "+virtual)
            rebuilt=tmp/f"rebuilt{k}.cpk"
            run([str(pack),"pack",str(root),"--output",str(rebuilt),
                "--template",str(stock),"--crilayla","--encrypt-storm"])
            new_index=inventory(rebuilt)
            oldpaths=[x["path"].casefold() for x in original_index["entries"]]
            newpaths=[x["path"].casefold() for x in new_index["entries"]]
            if oldpaths!=newpaths:
                raise FormatError("Rebuilder changed CPK file table layout/path order")
            check=tmp/f"verify{k}"
            run([str(exe),"--game",crypto,"-o",str(check),str(rebuilt)])
            verified=data_root(check)
            verified_paths={"data/"+file.relative_to(verified).as_posix().casefold()
                            for file in verified.rglob("*") if file.is_file()}
            if verified_paths!=set(expected_stock_paths):
                raise FormatError("Re-extracted CPK has extra or missing files")
            for virtual_key,original_digest in baseline_hashes.items():
                path=verified/expected_stock_paths[virtual_key].removeprefix("data/")
                if not path.is_file() or path.is_symlink() or sha(path)!=original_digest:
                    raise FormatError("Rebuilder changed untouched stock file: "+expected_stock_paths[virtual_key])
            for entry in manifest:
                path=verified/entry["virtual_path"].removeprefix("data/")
                if not path.is_file() or path.is_symlink() or sha(path)!=entry["sha256"]:
                    raise FormatError("Re-extraction changed XFBIN bytes: "+entry["virtual_path"])
            dst=ready/"atmosphere"/"contents"/TITLE/"romfs"/labels[k]
            dst.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(rebuilt,dst)
            results.append({"from":str(stock),"romfs_target":labels[k],"sha256":sha(dst),
                            "bytes":dst.stat().st_size,"replaced":len(manifest),
                            "verified_extracted":True,"untouched_files_verified":len(baseline_hashes)})
        incomplete=bool(scan["unported_runtime_dependencies"])
        report={"status":"INCOMPLETE_UNPORTED_MODDINGAPI" if incomplete else
                "CPKS_REEXTRACT_VERIFIED_NOT_GAMEPLAY_TESTED",
                "target_title":TITLE,"gameplay_tested":False,"arm64_api_present":False,
                "archives":results,"route_count":scan["matched_count"],
                "runtime_dependencies":scan["unported_runtime_dependencies"],
                "warning":"Neither XFBIN engine behavior nor Windows ModdingAPI hooks are certified."}
        (ready/"COMPILER_REPORT.json").write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf-8")
        if incomplete:
            (ready/"DO_NOT_INSTALL_AS_COMPLETE_MOD.txt").write_text(
                "Windows ModdingAPI and character hooks are not ported to ARM64.\n",encoding="utf-8")
        if not results:raise FormatError("No rebuilt CPKs created")
        os.replace(ready,target)
    return report

def main():
    a=argparse.ArgumentParser(description=__doc__)
    a.add_argument("--cpk",action="append",required=True)
    a.add_argument("--romfs",action="append",required=True)
    a.add_argument("--mod",action="append",required=True)
    a.add_argument("--fastcpk",required=True)
    a.add_argument("--rebuild",required=True)
    a.add_argument("--output",required=True)
    a.add_argument("--allow-unported",action="store_true")
    a.add_argument("--extract-game",choices=["auto","cc2-storm"],default="cc2-storm")
    args=a.parse_args()
    try:
        result=build(args.mod,args.cpk,args.romfs,args.fastcpk,args.rebuild,args.output,
                     allow_unported=args.allow_unported,crypto=args.extract_game)
        print(json.dumps(result,indent=2,ensure_ascii=False))
    except (FormatError,OSError,zipfile.BadZipFile,KeyError) as ex:
        sys.exit("ERROR: "+str(ex))

if __name__=="__main__":main()
