#!/usr/bin/env python3
"""Synthetic NSO0 and bounded LZ4 test fixtures; no game binaries redistributed."""
import struct,tempfile,unittest,zipfile
from pathlib import Path
from nso_170_guard import audit,load,decode_lz4,AuditError,BUILD_ID

def lit(data):
    n=len(data);out=bytearray([min(n,15)<<4])
    if n>=15:
        n-=15
        while n>=255:out.append(255);n-=255
        out.append(n)
    out.extend(data)
    return bytes(out)

def nso(text,compressed=True,bid=BUILD_ID):
    header=bytearray(0x100);header[:4]=b"NSO0"
    struct.pack_into("<I",header,12,1 if compressed else 0)
    struct.pack_into("<III",header,16,0x100,0,len(text))
    header[0x40:0x40+len(bid)]=bid
    packed=lit(text) if compressed else text
    struct.pack_into("<I",header,0x60,len(packed))
    return bytes(header)+packed

class NsoTests(unittest.TestCase):
    def test_literal(self):
        payload=b"hello ARM64"*30
        self.assertEqual(decode_lz4(lit(payload),len(payload)),payload)
    def test_overlapping_match(self):
        self.assertEqual(decode_lz4(bytes([0x10])+b"A"+bytes([1,0]),5),b"AAAAA")
    def test_invalid_offset(self):
        with self.assertRaises(AuditError):
            decode_lz4(bytes([0x10])+b"A"+bytes([2,0]),5)
    def test_matching_hook_and_buildid(self):
        text=bytearray(64);struct.pack_into("<I",text,8,0xD65F03C0)
        result=audit(nso(text),hooks={"DEMO":(8,[0xD65F03C0])})
        self.assertEqual(result["hooks_matched"],1)
        self.assertEqual(result["status"],"P32_FINGERPRINTS_MATCH_NOT_GAMEPLAY_TESTED")
    def test_uncompressed_text(self):
        text=bytearray(64);struct.pack_into("<I",text,8,0xD65F03C0)
        result=audit(nso(text,compressed=False),hooks={"DEMO":(8,[0xD65F03C0])})
        self.assertFalse(result["compressed"])
        self.assertEqual(result["hooks_matched"],1)
    def test_wrong_buildid(self):
        result=audit(nso(bytes(64),bid=b"\xff"*20),hooks={"DEMO":(8,[0])})
        self.assertTrue(result["status"].startswith("BLOCKED"))
    def test_wrong_instruction(self):
        result=audit(nso(bytes(64)),hooks={"DEMO":(8,[0xD65F03C0])})
        self.assertEqual(result["fingerprints"][0]["status"],"MISMATCH")
    def test_hook_out_of_range(self):
        result=audit(nso(bytes(64)),hooks={"TEST":(0x1206B4C,[0])})
        self.assertEqual(result["fingerprints"][0]["status"],"OUT_OF_RANGE")
    def test_zip_main(self):
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/"build.zip";binary=nso(bytes(64))
            with zipfile.ZipFile(path,"w") as z:z.writestr("exefs/main",binary)
            self.assertEqual(load(path),binary)
if __name__=="__main__":unittest.main()
