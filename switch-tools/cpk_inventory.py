#!/usr/bin/env python3
"""Read-only CRI CPK @UTF inventory, no game code or binary patches."""
import argparse,json,struct
from pathlib import Path

class FormatError(ValueError): pass
LIMIT=64<<20

def num(b,i,n):
    if i<0 or i+n>len(b):raise FormatError("Truncated number")
    return int.from_bytes(b[i:i+n],"big")

def cstr(b,i):
    if not 0<=i<len(b):raise FormatError("Invalid string offset")
    e=b.find(b"\0",i)
    if e<0:raise FormatError("Unterminated string")
    return b[i:e].decode("utf-8","replace")

def utf(b):
    if b[:4]!=b"@UTF" or len(b)<32:raise FormatError("Missing @UTF")
    end=num(b,4,4)+8
    if end>len(b) or end>LIMIT:raise FormatError("Invalid UTF length")
    b=b[:end]
    rows,strings,blob=(num(b,i,4)+8 for i in (8,12,16))
    cols=num(b,24,2);stride=num(b,26,2);count=num(b,28,4)
    if cols>256 or count>500000 or rows+count*stride>end or strings>end or blob>end:
        raise FormatError("UTF bounds invalid")
    def value(typ,pos):
        sizes=[1,1,2,2,4,4,8,8,4,8,4,8]
        if typ>=len(sizes):raise FormatError("Unsupported UTF type")
        n=sizes[typ]
        raw=b[pos:pos+n]
        if len(raw)!=n:raise FormatError("UTF value out of range")
        if typ==10:return cstr(b,strings+int.from_bytes(raw,"big")),pos+n
        if typ==11:
            start=blob+int.from_bytes(raw[:4],"big");size=int.from_bytes(raw[4:],"big")
            if start+size>end:raise FormatError("UTF data outside table")
            return b[start:start+size],pos+n
        if typ==8:return struct.unpack(">f",raw)[0],pos+n
        if typ==9:return struct.unpack(">d",raw)[0],pos+n
        return int.from_bytes(raw,"big",signed=typ in (1,3,5,7)),pos+n
    defs=[];pos=32
    for _ in range(cols):
        if pos>=end:raise FormatError("UTF column overflow")
        f=b[pos];pos+=1
        key=""
        if f&0x10:key=cstr(b,strings+num(b,pos,4));pos+=4
        const=None
        if f&0x20:const,pos=value(f&0xf,pos)
        defs.append((key,f,const))
    if pos>rows:raise FormatError("Columns overlap rows")
    output=[]
    for i in range(count):
        p=rows+i*stride;row={}
        for key,f,const in defs:
            if f&0x40:v,p=value(f&0xf,p)
            else:v=const
            if key:row[key]=v
        if p>rows+(i+1)*stride:raise FormatError("Row overflow")
        output.append(row)
    return output

def table(f,offset,size):
    if offset<0 or offset+16>size:raise FormatError("Chunk offset invalid")
    f.seek(offset);header=f.read(16)
    n=int.from_bytes(header[8:16],"little")
    if n>LIMIT or n<32 or offset+16+n>size:raise FormatError("Chunk size invalid")
    b=f.read(n)
    if b[:4]!=b"@UTF":
        a=bytearray(b);key=0x5f
        for i in range(len(a)):
            a[i]^=key;key=key*0x15&255
        b=bytes(a)
    return header[:4],utf(b)

def inventory(filename):
    p=Path(filename).resolve();size=p.stat().st_size
    with p.open("rb") as f:
        magic,head=table(f,0,size)
        if magic!=b"CPK " or not head:raise FormatError("Invalid CPK header")
        h=head[0];toc=int(h.get("TocOffset") or 0);content=int(h.get("ContentOffset") or 0)
        if not toc:raise FormatError("This version routes named TOC archives only; ITOC unsupported")
        magic,items=table(f,toc,size)
        if magic!=b"TOC ":raise FormatError("Invalid CPK TOC")
        base=min(toc,content) if content else toc
        out=[];seen=set()
        for i,r in enumerate(items):
            name="/".join(x for x in [(r.get("DirName") or "").rstrip("/"),r.get("FileName") or ""] if x)
            if not name or name.casefold() in seen or ".." in name.split("/"):
                raise FormatError("Duplicate or invalid TOC path")
            seen.add(name.casefold())
            o=int(r.get("FileOffset") or 0);stored=int(r.get("FileSize") or 0)
            if o<0 or stored<0 or base+o+stored>size:raise FormatError("File exceeds CPK")
            out.append({"path":name,"id":int(r.get("ID") or i),"stored_bytes":stored})
        return {"path":str(p),"bytes":size,"file_count":len(out),"entries":out}

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("archives",nargs="+")
    p.add_argument("-o","--output")
    args=p.parse_args()
    result={"format":"nsc_switch_cpk_index_v1","archives":[inventory(f) for f in args.archives]}
    txt=json.dumps(result,indent=2,ensure_ascii=False)
    if args.output:Path(args.output).write_text(txt,encoding="utf-8")
    else:print(txt)
