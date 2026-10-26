// @category NSC_Switch_Research
// Collect Ghidra pseudocode for functions referencing gameplay/ModdingAPI strings
// inside the official PC runtime DLL. This is read-only Windows x64 reference,
// not a Nintendo Switch ARM64 patch.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.address.Address;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.io.BufferedWriter;
import java.io.FileWriter;

public class RuntimeStringHooks extends GhidraScript {
    private boolean match(String s) {
        String str=s.toLowerCase(java.util.Locale.ROOT);
        return str.contains("moveset") || str.contains("moddingapi")
            || str.contains("player") || str.contains("jutsu")
            || str.contains("motion") || str.contains("lua")
            || str.contains("combo") || str.contains("character")
            || str.contains("projectile") || str.contains("awakening");
    }
    @Override
    public void run() throws Exception {
        String[] argv=getScriptArgs();
        if(argv.length<1)throw new IllegalArgumentException("Output filepath required");
        final int MAX=60, MAX_STRING_LENGTH=140;
        LinkedHashMap<Address,LinkedHashSet<String>> candidates=new LinkedHashMap<>();
        DataIterator it=currentProgram.getListing().getDefinedData(true);
        int strings=0,references=0;
        while(it.hasNext() && !monitor.isCancelled()) {
            Data d=it.next();
            if(!d.hasStringValue())continue;
            Object v=d.getValue();
            if(!(v instanceof String))continue;
            String value=(String)v;
            if(value.length()>MAX_STRING_LENGTH || !match(value))continue;
            strings++;
            ReferenceIterator refIt=currentProgram.getReferenceManager().getReferencesTo(d.getAddress());
            while(refIt.hasNext() && !monitor.isCancelled()) {
                Reference ref=refIt.next();
                Function fn=currentProgram.getFunctionManager().getFunctionContaining(ref.getFromAddress());
                if(fn==null)continue;
                references++;
                LinkedHashSet<String> values=candidates.get(fn.getEntryPoint());
                if(values==null){values=new LinkedHashSet<>();candidates.put(fn.getEntryPoint(),values);}
                values.add(value.replace("\n"," ").replace("\r"," ").replace("*/","* /"));
            }
        }
        DecompInterface decompiler=new DecompInterface();
        decompiler.toggleCCode(true);
        if(!decompiler.openProgram(currentProgram))
            throw new IllegalStateException("Ghidra decompiler unavailable");
        int written=0,complete=0;
        try(BufferedWriter out=new BufferedWriter(new FileWriter(argv[0]))){
            out.write("/* PC runtime Windows x64, Ghidra exploratory pseudocode ONLY. */\n");
            out.write("/* string_matches="+strings+" function_refs="+references+
                      " candidate_functions="+candidates.size()+" */\n");
            for(Address address:candidates.keySet()){
                if(monitor.isCancelled() || written>=MAX)break;
                Function fn=currentProgram.getFunctionManager().getFunctionAt(address);
                if(fn==null)continue;
                LinkedHashSet<String> vals=candidates.get(address);
                out.write("\n/* FUNCTION: "+fn.getName()+" @ "+address+" */\n");
                int num=0;
                for(String value:vals){
                    if(num++>=8)break;
                    out.write("/* matched string: "+value+" */\n");
                }
                written++;
                DecompileResults result=decompiler.decompileFunction(fn,30,monitor);
                if(result!=null && result.decompileCompleted() && result.getDecompiledFunction()!=null) {
                    complete++;
                    out.write(result.getDecompiledFunction().getC());
                }else out.write("/* DECOMPILATION INCOMPLETE */\n");
            }
            out.write("\n/* analyzed_functions="+written+" completed="+complete+" */\n");
        }finally{decompiler.dispose();}
        if(strings==0)throw new IllegalStateException("No suitable gameplay strings defined; analysis is incomplete");
        println("runtime x64 candidate functions="+candidates.size()+" selected="+written+" decompiled="+complete);
    }
}
