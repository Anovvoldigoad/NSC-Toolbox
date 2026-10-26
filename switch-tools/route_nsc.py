#!/usr/bin/env python3
"""Route .nsc XFBIN by exact source CPK entries; no inferred or fake IPS."""
from pathlib import Path
import argparse,hashlib,json,sys,zipfile
sys.path.insert(0,str(Path(__file__).resolve().parent))
from cpk_inventory import inventory,FormatError

def normalized(name):
    name=name.replace("\\","/")
    parts=[x for x in name.split("/") if x not in ("",".")]
    if not parts or ".." in parts or name.startswith("/") or (":" in parts[0]):
        raise FormatError("Untrusted ZIP path")
    return "/".join(parts)

def sha(path):
    h=hashlib.sha256()
    with Path(path).open("rb") as f:
        for b in iter(lambda:f.read(1<<20),b""):h.update(b)
    return h.hexdigest()

def route(mods,cpks):
    if not mods or not cpks:raise FormatError("Supply mod(s) and original CPK(s)")
    archives=[inventory(p) for p in cpks];mapping={}
    for archive in archives:
        for entry in archive["entries"]:
            mapping.setdefault(entry["path"].casefold(),[]).append(archive["path"])
    replacements={};missing=[];ambiguous=[];runtime=[];duplicates=0
    for mod in mods:
        p=Path(mod).resolve()
        if not zipfile.is_zipfile(p):raise FormatError("Expected unencrypted ZIP-based .nsc mod")
        with zipfile.ZipFile(p) as z:
            seen=set()
            for info in z.infolist():
                if info.is_dir():continue
                raw=normalized(info.filename);key=raw.casefold()
                if key in seen:raise FormatError("Duplicate source ZIP path")
                seen.add(key)
                if info.flag_bits&1:raise FormatError("Encrypted archive unsupported")
                if info.file_size<0 or info.file_size>(1<<30):
                    raise FormatError("ZIP expanded file size outside safe limit")
                if (key.startswith(("characters/","scripts/","plugins/","moddingapi/"))
                        or "/moddingapi/" in key
                        or key.endswith((".dll",".asi",".lua",".exe"))):
                    runtime.append({"mod":p.name,"path":raw,
                                    "reason":"PC-native/character/script dependency"})
                prefix="resources/files/"
                if not (key.startswith(prefix) and key.endswith(".xfbin")):continue
                virtual=normalized(raw[len(prefix):]);target=virtual.casefold()
                matches=mapping.get(target,[])
                if not matches:
                    missing.append({"mod":p.name,"path":virtual})
                    continue
                if len(matches)>1:
                    ambiguous.append({"mod":p.name,"path":virtual,"cpks":matches})
                    continue
                h=hashlib.sha256();size=0
                with z.open(info) as reader:
                    while True:
                        chunk=reader.read(1<<20)
                        if not chunk:break
                        size+=len(chunk)
                        if size>1<<30:raise FormatError("Expanded file too large")
                        h.update(chunk)
                if size!=info.file_size:raise FormatError("Expanded size mismatch")
                old=replacements.get(target)
                if old:
                    if old["sha256"]!=h.hexdigest():
                        raise FormatError("Mod collision for "+virtual)
                    duplicates+=1;continue
                replacements[target]={"virtual_path":virtual,"cpk":matches[0],
                                      "mod":p.name,"zip_entry":info.filename,"bytes":size,"sha256":h.hexdigest()}
    grouped={}
    for value in replacements.values():grouped.setdefault(value["cpk"],[]).append(value)
    for rows in grouped.values():rows.sort(key=lambda x:x["virtual_path"].casefold())
    status=("BLOCKED_UNKNOWN_OR_DUPLICATE_CPK" if (missing or ambiguous) else
            "BLOCKED_UNPORTED_MODDINGAPI" if runtime else
            "READY_FOR_CPK_REBUILD" if grouped else "NO_MATCHING_XFBIN")
    return {"status":status,"input_mods":[{"name":Path(m).name,"sha256":sha(m)} for m in mods],
            "stock_cpks":[{"path":a["path"],"files":a["file_count"]} for a in archives],
            "replacements":grouped,"matched_count":len(replacements),
            "duplicate_count":duplicates,"missing":missing,"ambiguous":ambiguous,
            "unported_runtime_dependencies":runtime,
            "warning":"Only CPK routing validated. Gameplay ARM64 ModdingAPI runtime remains unimplemented."}

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("--cpk",action="append",required=True)
    p.add_argument("--mod",action="append",required=True)
    p.add_argument("--output",required=True)
    args=p.parse_args()
    try:
        data=route(args.mod,args.cpk)
        Path(args.output).write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding="utf-8")
        print(json.dumps({"status":data["status"],"matched":data["matched_count"],
                          "missing":len(data["missing"]),"ambiguous":len(data["ambiguous"]),
                          "runtime_deps":len(data["unported_runtime_dependencies"])},indent=2))
        if data["status"].startswith("BLOCKED_"):raise SystemExit(2)
    except (FormatError,OSError,zipfile.BadZipFile) as e:
        sys.exit("ERROR: "+str(e))
