#!/usr/bin/env python3
"""Test build state machine with stub extractor/repacker, no gameplay claims."""
import shutil,tempfile,unittest,zipfile
from pathlib import Path
from unittest.mock import patch
from test_routing import cpk,nsc
from build_cpks import build,FormatError

class RebuildTests(unittest.TestCase):
    def test_atomic_multi_cpk_happy_path_mock_fastcpk(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d)
            original1=cpk(t,'one.cpk',['data/spc/a.xfbin'])
            original2=cpk(t,'two.cpk',['data/effect/b.xfbin'])
            m=nsc(t,'m.nsc',{'data/spc/a.xfbin':b'newA','data/effect/b.xfbin':b'newB'})
            extracted={}
            def simulate(command,timeout=1800):
                if 'pack' in command:
                    stock=Path(command[command.index('--template')+1])
                    output=Path(command[command.index('--output')+1])
                    shutil.copyfile(stock,output)
                    extracted[str(output)]=Path(command[2])
                else:
                    dest=Path(command[command.index('-o')+1])
                    src=Path(command[-1])
                    (dest/'data').mkdir(parents=True)
                    virt={'one.cpk':'spc/a.xfbin','two.cpk':'effect/b.xfbin'}
                    if src.name.startswith('rebuilt'):
                        stage=extracted[str(src)]
                        for path in stage.rglob('*.xfbin'):
                            final=dest/'data'/path.relative_to(stage)
                            final.parent.mkdir(parents=True,exist_ok=True)
                            shutil.copyfile(path,final)
                    else:
                        file=dest/'data'/virt[src.name]
                        file.parent.mkdir(parents=True,exist_ok=True)
                        file.write_bytes(b'OLD')
            outcome=t/'out'
            with patch('build_cpks.run',simulate):
                report=build([m],[original1,original2],['data/patch/170/common.cpk','data/launch/other.cpk'],
                             __file__,__file__,outcome)
            self.assertEqual(len(report['archives']),2)
            self.assertEqual(report['status'],'CPKS_REEXTRACT_VERIFIED_NOT_GAMEPLAY_TESTED')
            self.assertTrue((outcome/'COMPILER_REPORT.json').is_file())
            self.assertTrue((outcome/'atmosphere/contents/010084D00CF5E000/romfs/data/patch/170/common.cpk').is_file())

    def test_refuse_runtime_without_override(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d)
            a=cpk(t,'one.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'m.nsc',{'data/spc/a.xfbin':b'newA'},pc=True)
            with self.assertRaises(FormatError):
                build([m],[a],['data/patch/170/common.cpk'],__file__,__file__,t/'out')

    def test_fail_closed_before_publish(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d)
            a=cpk(t,'one.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'m.nsc',{'data/spc/a.xfbin':b'newA'})
            def failed(_cmd,timeout=1800):
                raise FormatError("Mock extractor failure")
            with patch('build_cpks.run',failed),self.assertRaises(FormatError):
                build([m],[a],['data/patch/170/common.cpk'],__file__,__file__,t/'out')
            self.assertFalse((t/'out').exists())

    def test_block_unknown_romfs_destination(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d)
            a=cpk(t,'one.cpk',['data/spc/a.xfbin'])
            m=nsc(t,'m.nsc',{'data/spc/a.xfbin':b'A'})
            with self.assertRaises(FormatError):
                build([m],[a],['data/../../bad.cpk'],__file__,__file__,t/'out')
if __name__=='__main__':unittest.main()
