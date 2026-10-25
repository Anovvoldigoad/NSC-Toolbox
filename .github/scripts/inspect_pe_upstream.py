#!/usr/bin/env python3
"""Static PE inventory of official NSC Mod Manager shipped binaries.
Reports metadata only, never redistributes DLLs or calls executable entry points.
"""
import hashlib
import json
import sys
from pathlib import Path
import pefile

TARGETS = {"nsc_modmanager.dll", "d3dcompiler_47.dll", "cpkloader.dll", "encryptcpk.dll"}

def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

def text(v):
    if isinstance(v, bytes):
        return v.decode("utf-8", "replace")
    return str(v)

def inventory(path):
    pe = pefile.PE(str(path), fast_load=False)
    imports = []
    for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
        imports.append({"module": text(entry.dll),
                        "functions": [text(x.name) if x.name else "#" + str(x.ordinal)
                                      for x in entry.imports]})
    exports = []
    exp = getattr(pe, "DIRECTORY_ENTRY_EXPORT", None)
    if exp:
        exports = [{"name": text(x.name) if x.name else None, "ordinal": x.ordinal,
                    "rva": hex(x.address), "forwarder": text(x.forwarder) if x.forwarder else None}
                   for x in exp.symbols]
    sections = [{"name": text(s.Name.split(b"\0")[0]),
                 "virtual_address": hex(s.VirtualAddress),
                 "size": s.Misc_VirtualSize,
                 "raw_size": s.SizeOfRawData}
                for s in pe.sections]
    return {"name": path.name,
            "source_sha256": digest(path), "file_size": path.stat().st_size,
            "machine": hex(pe.FILE_HEADER.Machine),
            "image_base": hex(pe.OPTIONAL_HEADER.ImageBase),
            "entry_point": hex(pe.OPTIONAL_HEADER.AddressOfEntryPoint),
            "imports": imports, "exports": exports, "sections": sections,
            "cli_metadata_present": bool(pe.OPTIONAL_HEADER.DATA_DIRECTORY[14].VirtualAddress)}

def main():
    root = Path(sys.argv[1]).resolve()
    out = Path(sys.argv[2]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    all_files = [p for p in root.rglob("*") if p.is_file() and p.name.casefold() in TARGETS]
    chosen = {}
    for item in all_files:
        key = item.name.casefold()
        if key not in chosen or len(item.parts) < len(chosen[key].parts):
            chosen[key] = item
    missing = TARGETS - set(chosen)
    if missing:
        raise SystemExit("Missing expected release binary: " + ", ".join(sorted(missing)))
    for name, path in sorted(chosen.items()):
        report = inventory(path)
        output = out / (name + ".pe.json")
        output.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(name, report["file_size"], len(report["imports"]), len(report["exports"]))
    selected = chosen["nsc_modmanager.dll"].resolve()
    (out / "managed_path.txt").write_text(str(selected), encoding="utf-8")

if __name__ == "__main__":
    main()
