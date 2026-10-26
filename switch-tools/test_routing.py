#!/usr/bin/env python3
"""Synthetic end-to-end test of CPK index and exact NSC routing."""
import tempfile,unittest,zipfile
from pathlib import Path
from cpk_inventory import utf,inventory,FormatError
from route_nsc import route

def make_utf(rows):
    keys=list(rows[0]);strings=bytearray(b'test\0');names={};vals={}
    for k in keys:names[k]=len(strings);strings.extend(k.encode()+b'\0')
    for r in rows:
        for v in r.values():
            if isinstance(v,str) and v not in vals:vals[v]=len(strings);strings.extend(v.encode()+b'\0')
    spec=[(k,10 if isinstance(rows[0][k],str) else 6) for k in keys]
    col=b''.join(bytes([0x50|t])+names[k].to_bytes(4,'big') for k,t in spec)
    data=b''.join((vals[r[k]].to_bytes(4,'big') if t==10 else int(r[k]).to_bytes(8,'big')) for r in rows for k,t in spec)
    start=32+len(col);head=bytearray(32);head[:4]=b'@UTF'
    head[8:12]=(start-8).to_bytes(4,'big')
    head[12:16]=(start+len(data)-8).to_bytes(4,'big')
    head[16:20]=(start+len(data)+len(strings)-8).to_bytes(4,'big')
    head[24:26]=len(keys).to_bytes(2,'big')
    head[26:28]=sum(4 if t==10 else 8 for _,t in spec).to_bytes(2,'big')
    head[28:32]=len(rows).to_bytes(4,'big')
    ret=bytes(head)+col+data+strings
    return ret[:4]+(len(ret)-8).to_bytes(4,'big')+ret[8:]

def chunk(magic,payload):return magic+b'\xff\0\0\0'+len(payload).to_bytes(8,'little')+payload

def cpk(folder,name,files):
    p=folder/name
    head=make_utf([{'ContentOffset':0x4000,'TocOffset':0x800}])
    rows=[]
    for i,path in enumerate(files):
        dr,fl=path.rsplit('/',1)
        rows.append({'DirName':dr,'FileName':fl,'FileOffset':i*0x10,'FileSize':4,'ID':i})
    with p.open('wb') as f:
        f.truncate(0x4000+len(files)*0x10+0x80)
        f.seek(0);f.write(chunk(b'CPK ',head))
        f.seek(0x800);f.write(chunk(b'TOC ',make_utf(rows)))
    return p

def nsc(folder,name,files,pc=False):
    p=folder/name
    with zipfile.ZipFile(p,'w') as z:
        for k,v in files.items():z.writestr('Resources/Files/'+k,v)
        if pc:z.writestr('Characters/code/moddingapi/base_game.dll',b'MZ')
    return p

class ParserTests(unittest.TestCase):
    def test_cpks_split_one_mod(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'a.cpk',['data/spc/a.xfbin']);b=cpk(t,'b.cpk',['data/effect/b.xfbin'])
            self.assertEqual(inventory(a)['file_count'],1)
            m=nsc(t,'test.nsc',{'data/spc/a.xfbin':b'A','data/effect/b.xfbin':b'B'})
            report=route([m],[a,b])
            self.assertEqual(report['status'],'READY_FOR_CPK_REBUILD')
            self.assertEqual(len(report['replacements']),2)
    def test_ambiguous_fails_closed(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'a.cpk',['data/spc/a.xfbin']);b=cpk(t,'b.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'test.nsc',{'data/spc/a.xfbin':b'A'})
            self.assertEqual(route([m],[a,b])['status'],'BLOCKED_UNKNOWN_OR_DUPLICATE_CPK')
    def test_missing_fails_closed(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'a.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'test.nsc',{'data/spc/b.xfbin':b'A'})
            self.assertEqual(len(route([m],[a])['missing']),1)
    def test_conflicting_mods_fail(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'a.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'one.nsc',{'data/spc/a.xfbin':b'A'})
            n=nsc(t,'two.nsc',{'data/spc/a.xfbin':b'B'})
            with self.assertRaises(FormatError):route([m,n],[a])
    def test_identical_mods_dedupe(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'a.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'one.nsc',{'data/spc/a.xfbin':b'A'})
            n=nsc(t,'two.nsc',{'data/spc/a.xfbin':b'A'})
            self.assertEqual(route([m,n],[a])['duplicate_count'],1)
    def test_runtime_dependency_blocked(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'a.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'one.nsc',{'data/spc/a.xfbin':b'A'},True)
            self.assertEqual(route([m],[a])['status'],'BLOCKED_UNPORTED_MODDINGAPI')
    def test_invalid_zip_entry_refused(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'a.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'one.nsc',{'data/../../../a.xfbin':b'A'})
            with self.assertRaises(FormatError):route([m],[a])
    def test_pc_lua_script_not_treated_as_cpk_only(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);a=cpk(t,'one.cpk',['data/spc/a.xfbin'])
            mod=t/'scripted.nsc'
            with zipfile.ZipFile(mod,'w') as z:
                z.writestr('Resources/Files/data/spc/a.xfbin',b'NUCC')
                z.writestr('Scripts/moveset.lua','function main() end')
            report=route([mod],[a])
            self.assertEqual(report['status'],'BLOCKED_UNPORTED_MODDINGAPI')
            self.assertEqual(len(report['unported_runtime_dependencies']),1)

    def test_malformed_cpk_refused(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);bad=t/'bad.cpk';bad.write_bytes(b'CPK ')
            with self.assertRaises(FormatError):inventory(bad)

if __name__=='__main__':unittest.main()
