package io.nsc.switchcompiler;

import org.json.JSONArray;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.zip.*;

/** Offline package staging; never pretends Windows ModdingAPI DLLs are Switch runtime. */
public final class Packager {
    private static final String TITLE="010084D00CF5E000";
    private static final String ROOT="atmosphere/contents/"+TITLE+"/romfs/";
    private static final long MAX_FILE=768L<<20, MAX_TOTAL=2L<<30;
    private Packager(){}
    private static final class Mapped {
        final String path,kind,warning;
        Mapped(String p,String k,String w){path=p;kind=k;warning=w;}
    }
    private static String normalize(String raw)throws IOException {
        if(raw==null||raw.isEmpty()||raw.indexOf('\0')>=0||
            raw.startsWith("/")||raw.matches("^[A-Za-z]:.*"))
            throw new IOException("Invalid archive path");
        ArrayList<String> parts=new ArrayList<>();
        for(String item:raw.replace('\\','/').split("/")) {
            if(item.equals(".."))throw new IOException("Directory traversal refused");
            if(!item.equals(".")&&!item.isEmpty())parts.add(item);
        }
        if(parts.isEmpty())throw new IOException("Empty archive entry");
        return String.join("/",parts);
    }
    private static Mapped map(String raw)throws IOException {
        String n=normalize(raw),l=n.toLowerCase(Locale.ROOT);
        String dst=null;
        if(l.startsWith(ROOT.toLowerCase(Locale.ROOT)))dst=n;
        else if(l.startsWith("romfs/"))dst="atmosphere/contents/"+TITLE+"/"+n;
        else if(l.startsWith("data/patch/170/")&&l.endsWith(".cpk"))dst=ROOT+n;
        if(dst!=null) {
            if(dst.toLowerCase(Locale.ROOT).endsWith(".cpk"))
                return new Mapped(dst,"cpk",null);
            return new Mapped("STAGING/UNVERIFIED_ROMFS/"+n,"pending","Raw ROMFS requires a loader or stock CPK merge");
        }
        if(l.startsWith("resources/files/"))
            return new Mapped("STAGING/PC_RESOURCES/"+n.substring("resources/files/".length()),"pending","PC XFBIN requires Switch CPK merge");
        if(l.startsWith("characters/"))
            return new Mapped("STAGING/CHARACTERS/"+n.substring("characters/".length()),"pending","Character addon parameters require porting");
        if(l.contains("moddingapi/")||l.endsWith(".dll"))
            return new Mapped("STAGING/PC_RUNTIME/"+n,"pending","Windows DLL cannot execute on Switch");
        if(l.equals("mod_config.ini")||l.equals("mod_icon.png"))
            return new Mapped("STAGING/METADATA/"+n,"metadata",null);
        return new Mapped("STAGING/UNKNOWN/"+n,"pending","Unmapped PC mod content");
    }
    private static String hash(byte[] data) {
        StringBuilder s=new StringBuilder();
        for(byte b:data)s.append(Character.forDigit((b>>4)&15,16)).append(Character.forDigit(b&15,16));
        return s.toString();
    }
    private static long copy(InputStream src,OutputStream dst,MessageDigest sha,long limit,
                             byte[] magic)throws IOException {
        byte[] buf=new byte[128*1024];int n;long count=0;int h=0;
        while((n=src.read(buf))!=-1){
            count+=n;if(count>limit||count>MAX_FILE)throw new IOException("Expanded input size limit");
            for(int j=0;j<n&&h<4;j++)magic[h++]=buf[j];
            sha.update(buf,0,n);dst.write(buf,0,n);
        }
        return count;
    }
    private static void append(ZipOutputStream result,InputStream src,Mapped mapped,
                Set<String> seen,JSONArray entries,Set<String> warnings,long[] total,int[] counts)
                throws Exception {
        if(!seen.add(mapped.path.toLowerCase(Locale.ROOT)))throw new IOException("Output path collision: "+mapped.path);
        MessageDigest sha=MessageDigest.getInstance("SHA-256");
        result.putNextEntry(new ZipEntry(mapped.path));
        byte[] magic=new byte[4];
        long count=copy(src,result,sha,MAX_TOTAL-total[0],magic);
        result.closeEntry();total[0]+=count;
        if(mapped.kind.equals("cpk")&&!Arrays.equals(magic,new byte[]{67,80,75,32}))
            throw new IOException("CPK header invalid: "+mapped.path);
        entries.put(new JSONObject().put("path",mapped.path).put("kind",mapped.kind)
            .put("bytes",count).put("sha256",hash(sha.digest())));
        if(mapped.warning!=null)warnings.add(mapped.warning);
        if(mapped.kind.equals("cpk"))counts[0]++;
        if(mapped.kind.equals("pending"))counts[1]++;
    }
    public static String compile(File input,String name,OutputStream target)throws Exception {
        String n=name.toLowerCase(Locale.ROOT);
        if(n.endsWith(".ips")||n.endsWith(".dll"))
            throw new IOException("IPS requires main NSO audit; PC DLL is not a Switch runtime");
        Set<String> seen=new HashSet<>(),warnings=new TreeSet<>();
        JSONArray entries=new JSONArray();int[] counts={0,0};long[] total={0};
        try(ZipOutputStream out=new ZipOutputStream(new BufferedOutputStream(target))) {
            if(n.endsWith(".cpk")) {
                if(!name.equalsIgnoreCase("common.cpk"))
                    throw new IOException("Raw CPK destination unknown; use Termux --cpk-target");
                try(InputStream stream=new FileInputStream(input)) {
                    append(out,stream,new Mapped(ROOT+"data/patch/170/common.cpk","cpk",null),
                        seen,entries,warnings,total,counts);
                }
            }else {
                try(ZipFile src=new ZipFile(input)) {
                    Enumeration<? extends ZipEntry> it=src.entries();int count=0;
                    while(it.hasMoreElements()){
                        ZipEntry e=it.nextElement();if(e.isDirectory())continue;
                        if(++count>100000)throw new IOException("Too many entries");
                        long size=e.getSize();
                        if(size<0||size>MAX_FILE||size>MAX_TOTAL-total[0]||e.getName().length()>4096)
                            throw new IOException("Invalid expanded ZIP size/path");
                        Mapped mapped=map(e.getName());
                        try(InputStream stream=src.getInputStream(e)){
                            append(out,stream,mapped,seen,entries,warnings,total,counts);
                        }
                    }
                }catch(ZipException e){
                    throw new IOException("Unsupported or encrypted container; needs ZIP-format .nsc",e);
                }
            }
            String status=counts[1]>0?"BLOCKED_UNPORTED_RESOURCES":
                counts[0]>0?"PACKAGED_NOT_GAMEPLAY_TESTED":"PREPARATION_ONLY";
            JSONObject report=new JSONObject().put("tool","NSC Native APK v0.5")
                .put("status",status).put("title_id",TITLE).put("source_name",name)
                .put("cpk_count",counts[0]).put("pending_count",counts[1])
                .put("file_count",entries.length()).put("entries",entries)
                .put("moddingapi_arm64_available",false)
                .put("warning","Packaging only; runtime gameplay not verified");
            JSONArray a=new JSONArray();for(String warning:warnings)a.put(warning);
            report.put("warnings",a);
            out.putNextEntry(new ZipEntry("COMPILER_REPORT.json"));
            out.write(report.toString(2).getBytes(StandardCharsets.UTF_8));out.closeEntry();
            if(counts[1]>0){
                out.putNextEntry(new ZipEntry("DO_NOT_INSTALL_AS_COMPLETE_MOD.txt"));
                out.write("Windows/PC resources were staged, NOT ported.\n".getBytes(StandardCharsets.UTF_8));
                out.closeEntry();
            }
            return status+"\nCPK="+counts[0]+", pending="+counts[1]+", files="+entries.length();
        }
    }
}
