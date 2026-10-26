// @category NSC_Switch_Research
// Read-only, documented exported entry points in the official upstream plugin.
// This file never patches the game and does not compile the pseudocode.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Symbol;
import java.io.BufferedWriter;
import java.io.FileWriter;
import java.util.HashSet;
import java.util.Arrays;

public class ExportHooks extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) throw new IllegalArgumentException("Output path missing");
        HashSet<String> keep = new HashSet<String>(Arrays.asList(
          "GameLoop","InitializeCommands","InitializeHooks",
          "InitializeLuaCommands","InitializePlugin","ParseApiFiles",
          "DllMain","DllMainCRTStartup"));
        DecompInterface api = new DecompInterface();
        api.toggleCCode(true);
        if (!api.openProgram(currentProgram)) throw new IllegalStateException("Cannot open Ghidra decompiler");
        int found = 0, completed = 0;
        try (BufferedWriter out = new BufferedWriter(new FileWriter(args[0]))) {
            out.write("/* Ghidra C pseudocode, original Windows x64; not an ARM64 port. */\n");
            for (Function func : currentProgram.getFunctionManager().getFunctions(true)) {
                if (monitor.isCancelled()) break;
                String name = func.getName();
                boolean selected = keep.contains(name) || name.startsWith("DllMain");
                if (!selected) {
                    for (Symbol symbol : currentProgram.getSymbolTable().getSymbols(func.getEntryPoint())) {
                        if (keep.contains(symbol.getName())) {
                            selected = true;
                            break;
                        }
                    }
                }
                if (!selected) continue;
                found++;
                out.write("\n/* Export " + name + " @ " + func.getEntryPoint() + " */\n");
                try {
                    DecompileResults res = api.decompileFunction(func, 45, monitor);
                    if (res != null && res.decompileCompleted() && res.getDecompiledFunction() != null) {
                        out.write(res.getDecompiledFunction().getC());
                        completed++;
                    } else out.write("/* DECOMPILER FAILED */\n");
                } catch (Exception e) {
                    out.write("/* DECOMPILER ERROR: "+e.getClass().getSimpleName()+" */\n");
                }
            }
            out.write("\n/* selected_functions="+found+" decompiled="+completed+" */\n");
        } finally {api.dispose();}
        if (found < 1) throw new IllegalStateException("No documented exports found");
        println("Export decompilation selected=" + found + " completed=" + completed);
    }
}
