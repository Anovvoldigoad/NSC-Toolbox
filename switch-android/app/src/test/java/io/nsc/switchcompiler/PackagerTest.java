package io.nsc.switchcompiler;

import static org.junit.Assert.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.*;
import java.util.zip.*;
import org.json.JSONObject;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.Config;

/** Real Java packager behavior, not Android UI instrumentation. */
@RunWith(RobolectricTestRunner.class)
@Config(sdk=28, manifest=Config.NONE)
public class PackagerTest {
    private File make(String name,String path,byte[] bytes)throws Exception {
        File file=Files.createTempFile("nsc_"+name+"_","_"+name).toFile();
        file.deleteOnExit();
        try(ZipOutputStream z=new ZipOutputStream(new FileOutputStream(file))){
            z.putNextEntry(new ZipEntry(path));
            z.write(bytes);z.closeEntry();
        }
        return file;
    }
    private JSONObject report(ByteArrayOutputStream data)throws Exception {
        try(ZipInputStream in=new ZipInputStream(new ByteArrayInputStream(data.toByteArray()))){
            for(ZipEntry e;(e=in.getNextEntry())!=null;){
                if(e.getName().equals("COMPILER_REPORT.json")){
                    ByteArrayOutputStream result=new ByteArrayOutputStream();
                    byte[] b=new byte[8192];int n;
                    while((n=in.read(b))!=-1)result.write(b,0,n);
                    return new JSONObject(result.toString("UTF-8"));
                }
            }
        }
        throw new AssertionError("Missing report");
    }
    @Test public void mergeSameCpkDeduplicates()throws Exception {
        String path="romfs/data/patch/170/common.cpk";
        byte[] valid="CPK padded-byte-example".getBytes(StandardCharsets.UTF_8);
        File a=make("one.nsc",path,valid),b=make("two.nsc",path,valid);
        ByteArrayOutputStream out=new ByteArrayOutputStream();
        String status=Packager.compileMulti(Arrays.asList(a,b),Arrays.asList("one.nsc","two.nsc"),out);
        assertTrue(status.contains("duplicates=1"));
        JSONObject j=report(out);
        assertEquals("PACKAGED_NOT_GAMEPLAY_TESTED",j.getString("status"));
        assertEquals(1,j.getInt("file_count"));
        assertEquals(1,j.getInt("deduplicated_entries"));
    }
    @Test public void conflictRefusesOneDestination()throws Exception {
        String path="romfs/data/patch/170/common.cpk";
        File a=make("one.nsc",path,"CPK First".getBytes(StandardCharsets.UTF_8));
        File b=make("two.nsc",path,"CPK Second".getBytes(StandardCharsets.UTF_8));
        try{
            Packager.compileMulti(Arrays.asList(a,b),Arrays.asList("one.nsc","two.nsc"),new ByteArrayOutputStream());
            fail("Expected a destination collision");
        }catch(IOException ex){
            assertTrue(ex.getMessage().contains("Conflicting assets"));
        }
    }
    @Test public void pcCharacterRemainsIncomplete()throws Exception {
        File a=make("character.nsc","Characters/newchar/data/spc/param.xfbin","NUCC".getBytes(StandardCharsets.UTF_8));
        ByteArrayOutputStream out=new ByteArrayOutputStream();
        Packager.compileMulti(Collections.singletonList(a),Collections.singletonList("character.nsc"),out);
        JSONObject j=report(out);
        assertEquals("BLOCKED_UNPORTED_RESOURCES",j.getString("status"));
        assertFalse(j.getBoolean("moddingapi_arm64_available"));
    }
    @Test public void traversalRejected()throws Exception {
        File a=make("evil.nsc","Resources/Files/../../evil.xfbin","NUCC".getBytes(StandardCharsets.UTF_8));
        try{
            Packager.compileMulti(Collections.singletonList(a),Collections.singletonList("evil.nsc"),new ByteArrayOutputStream());
            fail("Expected path validation failure");
        }catch(IOException ex){
            assertTrue(ex.getMessage().toLowerCase(Locale.ROOT).contains("traversal"));
        }
    }
}
