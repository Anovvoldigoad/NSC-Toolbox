#!/usr/bin/env python3
"""Read-only NSO main v1.70 Build ID and P32 ARM64 instruction audit.
No patch is applied. Matching existing hooks does not port ModdingAPI.
"""
import argparse, hashlib, json, struct, sys, zipfile
from pathlib import Path

BUILD_ID=bytes.fromhex("48ece454b61412b9fb46fab2be3f5ef7b2804f39")
HOOKS={
 "CPK_BIND":(0x473190,[0xF81D0FFE,0xA90157F6,0xA9024FF4,0xD000E6A8,0xF945ED08,0xAA0003F5,0xF9400100,0xB4000200]),
 "CHAR":(0x3F4150,[0xF000EA68,0xF9424508,0xF9760908,0x2A0003E1,0xF9409500,0x1410AC93]),
 "LOAD_REQ":(0x1206B4C,[0xF81D0FFE,0xA90157F6,0xA9024FF4,0xF9400008,0xAA0203F4,0xAA0103F6,0xAA0003F3,0xF9400908]),
 "LOAD_CREATE":(0x1206C9C,[0xD102C3FF,0xF9003BFE,0xA9085FF8,0xA90957F6,0xA90A4FF4,0xAA0203F6,0xAA0103F5,0xAA0003F4]),
 "LOAD_STATUS":(0x1207EFC,[0xF81F0FFE,0x97FFFB36,0xB4000060,0xF84107FE,0x17FFF9D8,0x52800080,0xF84107FE,0xD65F03C0]),
 "CHUNK":(0x3EAE70,[0xD100C3FF,0xA90157FE,0xA9024FF4,0xAA0003F5,0xB000EAC0,0xF942F000,0xAA0103F3,0xAA1503E1]),
 "PROCESS":(0x116F404,[0xA9BC7BFD,0xA9015FF8,0xA90257F6,0xA9034FF4,0xD10A03FF,0xD0019008,0xB9807058,0xAA0003F5]),
 "FILE_OPEN":(0x1170FB0,[0xA9BD5FFE,0xA90157F6,0xA9024FF4,0x6F00E400,0xAA0003F6,0xB0007ED7,0x3C838EC0,0xB90106C2]),
}
MAX=192<<20

class AuditError(ValueError): pass

def u32(data, offset):
    if offset<0 or offset+4>len(data):raise AuditError("Truncated NSO header")
    return struct.unpack_from("<I",data,offset)[0]

def decode_lz4(data,expected):
    if expected<0 or expected>MAX:raise AuditError("Invalid decompressed size")
    i=0;out=bytearray()
    while i<len(data):
        token=data[i];i+=1
        n=token>>4
        if n==15:
            while True:
                if i>=len(data):raise AuditError("LZ4 literal length truncated")
                a=data[i];i+=1;n+=a
                if a!=255:break
        if n>expected-len(out) or i+n>len(data):raise AuditError("LZ4 literals invalid")
        out.extend(data[i:i+n]);i+=n
        if i==len(data):break
        if i+2>len(data):raise AuditError("LZ4 offset truncated")
        distance=data[i]|(data[i+1]<<8);i+=2
        if distance<1 or distance>len(out):raise AuditError("LZ4 offset invalid")
        n=(token&15)+4
        if token&15==15:
            while True:
                if i>=len(data):raise AuditError("LZ4 match length truncated")
                a=data[i];i+=1;n+=a
                if a!=255:break
        if n>expected-len(out):raise AuditError("LZ4 output too long")
        seed=bytes(out[-distance:])
        out.extend(seed[:n] if n<=distance else (seed*((n+distance-1)//distance))[:n])
    if len(out)!=expected:raise AuditError("LZ4 output length mismatch")
    return bytes(out)

def load(path):
    path=Path(path)
    if path.suffix.casefold()!=".zip":
        if path.stat().st_size>MAX:raise AuditError("NSO too large")
        return path.read_bytes()
    with zipfile.ZipFile(path) as z:
        members=[x for x in z.infolist() if not x.is_dir() and
                 x.filename.replace("\\","/").split("/")[-1]=="main"]
        if len(members)!=1:raise AuditError("ZIP must have exactly one main NSO")
        if members[0].file_size>MAX:raise AuditError("NSO ZIP entry too large")
        with z.open(members[0]) as src:
            data=src.read(MAX+1)
        if len(data)>MAX:raise AuditError("Extracted NSO too large")
        return data

def audit(data,hooks=HOOKS,expected_build_id=BUILD_ID):
    if len(data)<0x100 or data[:4]!=b"NSO0":raise AuditError("Invalid NSO0")
    compressed=(u32(data,12)&1)!=0
    file_off,mem_off,unpacked=(u32(data,n) for n in (0x10,0x14,0x18))
    stored=u32(data,0x60) if compressed else unpacked
    if file_off<0x100 or unpacked>MAX or stored>MAX or not stored or not unpacked:
        raise AuditError("NSO text bounds invalid")
    if file_off+stored>len(data):raise AuditError("NSO text data truncated")
    raw=data[file_off:file_off+stored]
    text=decode_lz4(raw,unpacked) if compressed else raw
    build_id=data[0x40:0x60]
    matches=[]
    for name,(address,words) in hooks.items():
        offset=address-mem_off
        expected=b"".join(struct.pack("<I",w) for w in words)
        actual=text[offset:offset+len(expected)] if offset>=0 else b""
        state=("OUT_OF_RANGE" if offset<0 or offset+len(expected)>len(text)
               else "MATCH" if actual==expected else "MISMATCH")
        matches.append({"hook":name,"address":hex(address),"status":state,
                        "expected":expected.hex(),"actual":actual.hex()})
    correct_id=build_id.startswith(expected_build_id)
    passed=correct_id and all(x["status"]=="MATCH" for x in matches)
    return {"status":"P32_FINGERPRINTS_MATCH_NOT_GAMEPLAY_TESTED" if passed
            else "BLOCKED_BUILD_ID_OR_INSTRUCTION_MISMATCH",
            "build_id":build_id.hex(),"expected_build_id_prefix":expected_build_id.hex(),
            "build_id_matches":correct_id,"compressed":compressed,
            "text_memory_offset":hex(mem_off),"text_bytes":len(text),
            "nso_sha256":hashlib.sha256(data).hexdigest(),
            "text_sha256":hashlib.sha256(text).hexdigest(),
            "hooks_checked":len(matches),
            "hooks_matched":sum(x["status"]=="MATCH" for x in matches),
            "fingerprints":matches,"moddingapi_arm64_complete":False,
            "gameplay_tested":False,
            "note":"Only earlier P32 hook fingerprints; does not install or implement any hooks."}

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--nso",required=True)
    ap.add_argument("--output")
    args=ap.parse_args()
    try:
        report=audit(load(args.nso))
        output=json.dumps(report,indent=2)
        if args.output:Path(args.output).write_text(output,encoding="utf-8")
        else:print(output)
        if report["status"].startswith("BLOCKED"):sys.exit(2)
    except (AuditError,OSError,zipfile.BadZipFile) as e:
        ap.exit(2,"ERROR: "+str(e)+"\n")

if __name__=="__main__":main()
