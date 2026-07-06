using System.Collections.Generic;

namespace XonoticGodot.Engine.Console;

/// <summary>
/// [T70] Client debug verbs ported from QC <c>client/command/cl_cmd.qc</c>: <c>blurtest, boxparticles,
/// create_scrshot_ent, debugmodel, localprint, mv_download, print_cptimes</c>.
///
/// These are CSQC verbs whose effects (postprocess blur, manual particle spawns, an autoscreenshot
/// entity written from the client view, debug-model drawables, map-vote mapshot download, race
/// checkpoint splits) live in the client rendering / HUD / particle layer — the Godot-side <c>game/</c>
/// code — not this zero-Godot engine console. Per the T70 acceptance criteria, render-dependent client
/// verbs are registered and callable but are no-ops that print their faithful QC usage plus an honest
/// "not implemented in this layer" note. Kept in a partial file so <c>ConsoleCommands.cs</c> stays put.
/// </summary>
public sealed partial class ConsoleCommands
{
    /// <summary>Register the T70 client debug verbs. Called once from <c>Register()</c>.</summary>
    private void RegisterClientDebug()
    {
        _interp.RegisterCommand("blurtest", CmdBlurtest);
        _interp.RegisterCommand("boxparticles", CmdBoxParticles);
        _interp.RegisterCommand("create_scrshot_ent", CmdCreateScrshotEnt);
        _interp.RegisterCommand("debugmodel", CmdDebugModel);
        _interp.RegisterCommand("localprint", CmdLocalPrint);
        _interp.RegisterCommand("mv_download", CmdMvDownload);
        _interp.RegisterCommand("print_cptimes", CmdPrintCpTimes);
    }

    private void CmdBlurtest(IReadOnlyList<string> a)
        => _print("blurtest is not enabled on this client (QC: gated behind -DBLURTEST; the blur postprocess is a host render feature).");

    private void CmdBoxParticles(IReadOnlyList<string> a)
    {
        if (a.Count != 9)
        {
            _print("Usage: boxparticles <effectname> <owner> <org_from> <org_to> <dir_from> <dir_to> <countmultiplier> <flags>");
            return;
        }
        _print("boxparticles: not implemented in the engine console — manual particle spawning is client-render (game/) side.");
    }

    private void CmdCreateScrshotEnt(IReadOnlyList<string> a)
        => _print("create_scrshot_ent: not implemented here — it writes an info_autoscreenshot entity from the client view origin/angles, which are host-render state (game/).");

    private void CmdDebugModel(IReadOnlyList<string> a)
    {
        if (a.Count < 2) { _print("Usage: debugmodel <model>"); return; }
        _print($"debugmodel: not implemented in the engine console — spawning a debug-model drawable ('{a[1]}') is client-render (game/) side.");
    }

    private void CmdLocalPrint(IReadOnlyList<string> a)
    {
        if (a.Count < 2) { _print("Usage: localprint \"<message>\""); return; }
        // QC: centerprint_AddStandard(msg) — a HUD centerprint to yourself. No HUD in this layer, so
        // echo to the console (so the text is at least visible) and note the true HUD path.
        _print(a[1]);
        _print("(localprint: shown in console; the HUD centerprint is a game/ render concern.)");
    }

    private void CmdMvDownload(IReadOnlyList<string> a)
    {
        if (a.Count < 2) { _print("Usage: mv_download <mapid>"); return; }
        _print("mv_download: not implemented — mapshot download for the map-vote screen is owned by the (unported) mapvote UI.");
    }

    private void CmdPrintCpTimes(IReadOnlyList<string> a)
        => _print("print_cptimes: not implemented in the engine console — race checkpoint splits are client race-HUD state (game/).");
}
