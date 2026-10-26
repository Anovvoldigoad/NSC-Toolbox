package io.nsc.switchcompiler;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.content.ClipData;
import java.util.ArrayList;
import java.util.List;
import android.os.Bundle;
import android.database.Cursor;
import android.provider.OpenableColumns;
import android.widget.*;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;

public final class MainActivity extends Activity {
    private static final int PICK=101, SAVE=102;
    private final ExecutorService pool=Executors.newSingleThreadExecutor();
    private final ArrayList<Uri> mods=new ArrayList<>();
    private final ArrayList<String> modNames=new ArrayList<>();
    private TextView result;
    private Button export;
    private int dp(int n){return (int)(getResources().getDisplayMetrics().density*n+0.5f);}
    private void status(String s){runOnUiThread(()->result.setText(s));}
    @Override public void onCreate(Bundle b) {
        super.onCreate(b);
        LinearLayout root=new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);root.setPadding(dp(18),dp(22),dp(18),dp(22));
        TextView heading=new TextView(this);heading.setText("NSC → SWITCH 1.70");heading.setTextSize(24);root.addView(heading);
        TextView info=new TextView(this);
        info.setText("Compiler Android v0.6. Pilih beberapa .nsc/.zip atau CPK Switch, gabungkan file identik, dan blokir konflik. Hasil STAGING bukan playable; ModdingAPI ARM64 belum ada.\n");
        info.setTextSize(15);root.addView(info);
        Button upstream=new Button(this);upstream.setText("CEK MODDINGAPI RESMI");root.addView(upstream);
        upstream.setOnClickListener(v->{
            upstream.setEnabled(false);status("Membaca rilis ModdingAPI resmi...");
            pool.execute(()->{
                try{
                    HttpURLConnection c=(HttpURLConnection)new URL("https://api.github.com/repos/TheLeonX/NSC-ModManager/releases/latest").openConnection();
                    c.setRequestProperty("Accept","application/vnd.github+json");
                    c.setConnectTimeout(15000);c.setReadTimeout(20000);
                    if(c.getResponseCode()!=200)throw new IOException("HTTP "+c.getResponseCode());
                    ByteArrayOutputStream bytes=new ByteArrayOutputStream();
                    try(InputStream in=c.getInputStream()){
                        byte[] buf=new byte[8192];int n;
                        while((n=in.read(buf))!=-1) {
                            if(bytes.size()+n>200000)throw new IOException("Metadata too large");
                            bytes.write(buf,0,n);
                        }
                    }finally{c.disconnect();}
                    JSONObject j=new JSONObject(new String(bytes.toByteArray(),StandardCharsets.UTF_8));
                    status("Latest upstream: "+j.optString("tag_name")+
                        "\nLibrary Windows yang disertakan pada rilis tidak kompatibel dengan Switch ARM64.");
                }catch(Exception e){status("Tidak bisa cek rilis: "+e.getMessage());}
                finally{runOnUiThread(()->upstream.setEnabled(true));}
            });
        });
        Button pick=new Button(this);pick.setText("1. PILIH SATU / BEBERAPA MOD");root.addView(pick);
        pick.setOnClickListener(v->{
            Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.setType("*/*");i.addCategory(Intent.CATEGORY_OPENABLE);
            i.putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true);
            startActivityForResult(i,PICK);
        });
        export=new Button(this);export.setText("2. BUAT PAKET ZIP");export.setEnabled(false);root.addView(export);
        export.setOnClickListener(v->{
            Intent i=new Intent(Intent.ACTION_CREATE_DOCUMENT);
            i.setType("application/zip");i.addCategory(Intent.CATEGORY_OPENABLE);
            i.putExtra(Intent.EXTRA_TITLE,"NSC_Switch_v06_Bundle.zip");
            startActivityForResult(i,SAVE);
        });
        result=new TextView(this);result.setTextIsSelectable(true);
        result.setText("Pilih berkas untuk divalidasi. Tidak perlu PC.");
        root.addView(result);
        ScrollView scroll=new ScrollView(this);scroll.addView(root);setContentView(scroll);
    }
    private String filename(Uri u){
        try(Cursor c=getContentResolver().query(u,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){
            if(c!=null&&c.moveToFirst())return c.getString(0);
        }catch(Exception ignored){}
        return "unknown.zip";
    }
    @Override @SuppressWarnings("deprecation")
    protected void onActivityResult(int request,int response,Intent data){
        super.onActivityResult(request,response,data);
        if(response!=RESULT_OK || data==null)return;
        if(request==PICK){
            mods.clear();modNames.clear();
            ClipData chosen=data.getClipData();
            if(chosen!=null) {
                for(int i=0;i<chosen.getItemCount() && i<32;i++){
                    Uri value=chosen.getItemAt(i).getUri();
                    if(value!=null && !mods.contains(value)){mods.add(value);modNames.add(filename(value));}
                }
            }else if(data.getData()!=null){
                mods.add(data.getData());modNames.add(filename(data.getData()));
            }
            export.setEnabled(!mods.isEmpty());
            StringBuilder names=new StringBuilder("Terpilih: "+mods.size()+" file\n");
            for(String name:modNames)names.append(name).append('\n');
            status(names.toString()+"Belum ada file yang diubah.");
        }else if(request==SAVE && data.getData()!=null && !mods.isEmpty()){
            compile(data.getData());
        }
    }
    private void compile(Uri target){
        export.setEnabled(false);status("Memvalidasi "+mods.size()+" mod...");
        final ArrayList<Uri> selection=new ArrayList<>(mods);
        final ArrayList<String> names=new ArrayList<>(modNames);
        pool.execute(()->{
            ArrayList<File> temps=new ArrayList<>();
            File outfile=null;
            try{
                long combined=0;
                for(int i=0;i<selection.size();i++){
                    File src=File.createTempFile("nsc_mod_",".bin",getCacheDir());
                    temps.add(src);
                    try(InputStream in=getContentResolver().openInputStream(selection.get(i));
                        OutputStream out=new FileOutputStream(src)){
                        if(in==null)throw new IOException("Input tidak bisa dibaca: "+names.get(i));
                        byte[] buf=new byte[131072];int n;long size=0;
                        while((n=in.read(buf))!=-1){
                            size+=n;combined+=n;
                            if(size>(768L<<20) || combined>(2L<<30))
                                throw new IOException("Batas penyimpanan input terlampaui");
                            out.write(buf,0,n);
                        }
                    }
                }
                outfile=File.createTempFile("nsc_compiled_",".zip",getCacheDir());
                String report;
                try(OutputStream out=new FileOutputStream(outfile)){
                    report=Packager.compileMulti(temps,names,out);
                }
                try(InputStream in=new FileInputStream(outfile);
                    OutputStream out=getContentResolver().openOutputStream(target,"w")){
                    if(out==null)throw new IOException("Output tidak bisa ditulis");
                    byte[] b=new byte[131072];int n;
                    while((n=in.read(b))!=-1)out.write(b,0,n);
                }
                status("Paket berhasil disimpan.\n"+report+
                    "\nBaca COMPILER_REPORT.json. Paket STAGING belum siap dimainkan.");
            }catch(Exception e){
                status("GAGAL: "+e.getMessage()+"\nHapus ZIP output yang mungkin kosong/parsial.");
            }finally{
                for(File f:temps)f.delete();
                if(outfile!=null)outfile.delete();
                runOnUiThread(()->export.setEnabled(!mods.isEmpty()));
            }
        });
    }
    @Override protected void onDestroy(){pool.shutdownNow();super.onDestroy();}
}
