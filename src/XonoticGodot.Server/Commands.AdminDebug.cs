using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using XonoticGodot.Common.Framework;
using XonoticGodot.Common.Gameplay;
using XonoticGodot.Common.Services;

namespace XonoticGodot.Server;

/// <summary>
/// [T70] Admin / debug / utility command tail — the C# port of the unimplemented server verbs in
/// QC <c>server/command/sv_cmd.qc</c>: <c>adminmsg, stuffto, radarmap, make_mapinfo, gettaginfo,
/// printstats, database, delrec, effectindexdump, anticheat, bbox, trace</c>.
///
/// Registered through the same <see cref="Commands.Register(string,string,Func{CommandContext,bool})"/>
/// table as every other server verb. (The project has no <c>[ConsoleCommand]</c> source generator for
/// console verbs — <c>XonoticGodot.SourceGen</c>'s RegistryGenerator only enrolls type registries like
/// weapons/net-props — so the table IS the registration path here.)
///
/// Kept in this partial file to stay off the <c>Commands.cs</c> hot-file body (TODO.md conflict map:
/// <c>Commands.cs</c> is shared by T38/T46/T47/T56/T60/T70). Verbs whose behaviour needs a subsystem the
/// port doesn't expose (map extents, radar/model rendering, particle/effect registry, serverprogs DB)
/// print their faithful QC usage plus an honest "not wired/implemented" note, per the T70 spec.
/// </summary>
public sealed partial class Commands
{
    /// <summary>
    /// Host hook: deliver a raw console-command string to one client (QC <c>stuffcmd(client, ...)</c>).
    /// Null until the host wires a per-client command channel; used by <c>stuffto</c> and the
    /// <c>adminmsg</c> infobar path. Mirrors the existing <c>ChatToPlayer</c>/<c>ChatBroadcast</c> hooks.
    /// </summary>
    public Action<Player, string>? StuffToClient { get; set; }

    /// <summary>Register the T70 admin/debug/utility verbs. Called once from <c>RegisterBuiltins()</c>.</summary>
    private void RegisterAdminDebug()
    {
        // QC stuffto is gated behind -DSTUFFTO_ENABLED; mirror that with an opt-in cvar (default off).
        Cvars.Register("sv_cmd_stuffto_enabled", "0");

        Register("adminmsg", "adminmsg <clients> \"<message>\" [<infobartime>] — send an admin message to specific clients", CmdAdminMsg);
        Register("anticheat", "anticheat <client> — create an anticheat report for a client", CmdAnticheat);
        Register("bbox", "bbox <client> — print a client's bounding box (see help for the world-size form)", CmdBbox);
        Register("database", "database <save|dump|load> <filename> — serverprogs database controls", CmdDatabase);
        Register("delrec", "delrec <ranking> [<map>] — delete race time record(s) for a map", CmdDelRec);
        Register("effectindexdump", "effectindexdump — dump the particle effect index table", CmdEffectIndexDump);
        Register("gettaginfo", "gettaginfo <model> <frame> <index> [<cmd1>] [<cmd2>] — query a model tag", CmdGetTagInfo);
        Register("make_mapinfo", "make_mapinfo — rebuild mapinfo files", CmdMakeMapInfo);
        Register("printstats", "printstats — dump player stats and score information", CmdPrintStats);
        Register("radarmap", "radarmap [options] — generate a radar image of the map", CmdRadarMap);
        Register("stuffto", "stuffto <client> \"<command>\" — send a console command to a client", CmdStuffTo);
        Register("trace", "trace <start> <end> | trace <showline|walk|debug|debug2> ... — tracing debug tools", CmdTrace);
    }

    // ---------------------------------------------------------------------------------------------
    // adminmsg — QC GameCommand_adminmsg (sv_cmd.qc): message a comma/space list of clients.
    // ---------------------------------------------------------------------------------------------
    private bool CmdAdminMsg(CommandContext ctx)
    {
        string targetsArg = ctx.Arg(1);
        string message = ctx.Arg(2);
        if (targetsArg == "" || message == "")
        {
            ctx.Print("Usage: adminmsg <clients> \"<message>\" [<infobartime>]");
            ctx.Print("  <clients> is a comma/space separated list of player ids or names.");
            ctx.Print("  With <infobartime> the message goes to the infobar; otherwise as a centerprint.");
            return true;
        }
        float infobarTime = ctx.ArgFloat(3);

        var sent = new List<string>();
        foreach (string tok in targetsArg.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            Player? client = FindPlayerByNameOrId(tok);
            if (client is null) { ctx.Print($"adminmsg: no such client '{tok}', skipping."); continue; }

            if (infobarTime > 0f)
            {
                // QC: stuffcmd(client, "infobar <time> \"<msg>\"") — needs the per-client command channel.
                StuffToClient?.Invoke(client,
                    $"infobar {infobarTime.ToString("0.######", CultureInfo.InvariantCulture)} \"{AdmConsoleSafe(message)}\"");
            }
            else
            {
                // QC: centerprint(client, ...) + sprint(client, ...). The centerprint is a HUD concern
                // (game/ render side); the sprint (console line) is delivered reliably via ChatToPlayer.
                ChatToPlayer?.Invoke(client, $"^3{AdminCallerName(ctx)}^7: {message}");
            }
            sent.Add(client.NetName);
        }

        if (sent.Count > 0) ChatBroadcast?.Invoke($"Successfully sent message '{message}' to {string.Join(", ", sent)}.");
        else ctx.Print($"No players given ({targetsArg}) could receive the message.");
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // anticheat — QC GameCommand_anticheat → anticheat_report_to_eventlog(client).
    // ---------------------------------------------------------------------------------------------
    private bool CmdAnticheat(CommandContext ctx)
    {
        Player? client = FindPlayerByNameOrId(ctx.Arg(1));
        if (client is null)
        {
            ctx.Print("Usage: anticheat <client>");
            ctx.Print("  <client> is the entity number or name of the player.");
            return true;
        }
        _world.AntiCheat.ReportToEventLog(client, _world.Time, line => { ctx.Print(line); ChatConsole?.Invoke(line); });
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // bbox — QC GameCommand_bbox prints the map's solid box (no args). The T70 spec redefines this as
    // "bbox <entity>: print the entity's bounding box", which is fully portable; the no-arg world form
    // needs a map-extents accessor the port doesn't expose yet.
    // ---------------------------------------------------------------------------------------------
    private bool CmdBbox(CommandContext ctx)
    {
        if (ctx.Arg(1) != "")
        {
            Player? e = FindPlayerByNameOrId(ctx.Arg(1));
            if (e is null) { ctx.Print($"bbox: no such client '{ctx.Arg(1)}'."); return true; }
            ctx.Print($"entity #{e.PlayerId} ({e.NetName})");
            ctx.Print($"  origin:            {AdmFmt(e.Origin)}");
            ctx.Print($"  size mins..maxs:   {AdmFmt(e.Mins)} .. {AdmFmt(e.Maxs)}");
            ctx.Print($"  absolute box:      {AdmFmt(e.AbsMin)} .. {AdmFmt(e.AbsMax)}");
            return true;
        }
        ctx.Print("Usage: bbox <client>   (prints that client's bounding box)");
        ctx.Print("  Note: the QC no-argument 'world size' form (seeded from world.absmin/absmax) needs a");
        ctx.Print("  map-extents accessor that isn't exposed in the port yet.");
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // trace — QC GameCommand_trace is a debug multiplexer (debug/debug2/walk/showline). The T70 spec
    // wants "trace <start> <end> → traceline + print". We support both: the QC subcommands plus the
    // bare two-vector form.
    // ---------------------------------------------------------------------------------------------
    private bool CmdTrace(CommandContext ctx)
    {
        switch (ctx.Arg(1))
        {
            case "showline":
                return TraceShowline(ctx, ctx.Arg(2), ctx.Arg(3));
            case "walk":
                ctx.Print("trace walk: not ported — tracewalk() (bot pathing sweep) has no port analog yet.");
                return true;
            case "debug":
            case "debug2":
                ctx.Print($"trace {ctx.Arg(1)}: not ported — a Darkplaces engine-breakpoint stress harness (infinite loop + VM_rint breakpoint), inappropriate to reproduce.");
                return true;
            case "":
                ctx.Print("Usage: trace <start> <end>            — traceline and print the result");
                ctx.Print("       trace showline <start> <end>   — same (QC subcommand form)");
                ctx.Print("       trace walk|debug|debug2        — QC debug tools (not ported)");
                ctx.Print("  <start>/<end> are \"x y z\" world coordinates.");
                return true;
            default:
                return TraceShowline(ctx, ctx.Arg(1), ctx.Arg(2)); // bare "trace <start> <end>"
        }
    }

    private bool TraceShowline(CommandContext ctx, string startTok, string endTok)
    {
        if (!AdmTryParseVec(startTok, out Vector3 start) || !AdmTryParseVec(endTok, out Vector3 end))
        {
            ctx.Print("trace: could not parse <start>/<end> as \"x y z\" vectors.");
            return true;
        }
        TraceResult t = Api.Trace.Trace(start, Vector3.Zero, Vector3.Zero, end, MoveFilter.Normal, null);
        ctx.Print($"traceline {AdmFmt(start)} -> {AdmFmt(end)}");
        ctx.Print($"  fraction:   {t.Fraction.ToString("0.######", CultureInfo.InvariantCulture)}");
        ctx.Print($"  endpos:     {AdmFmt(t.EndPos)}");
        ctx.Print($"  normal:     {AdmFmt(t.PlaneNormal)}");
        ctx.Print($"  startsolid: {t.StartSolid}   allsolid: {t.AllSolid}");
        ctx.Print($"  contents:   0x{t.DpHitContents:x}   q3flags: 0x{t.DpHitQ3SurfaceFlags:x}");
        if (t.DpHitTextureName is not null) ctx.Print($"  texture:    {t.DpHitTextureName}");
        if (t.Ent is not null) ctx.Print($"  hit entity: {t.Ent.ClassName}#{t.Ent.Index}");
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // stuffto — QC GameCommand_stuffto: send a console command to one client (dangerous; opt-in).
    // ---------------------------------------------------------------------------------------------
    private bool CmdStuffTo(CommandContext ctx)
    {
        if (!Cvars.Bool("sv_cmd_stuffto_enabled"))
        {
            ctx.Print("stuffto is not enabled on this server (set sv_cmd_stuffto_enabled 1 to allow).");
            return true;
        }
        string command = ctx.Arg(2);
        if (ctx.Arg(1) == "" || command == "")
        {
            ctx.Print("Usage: stuffto <client> \"<command>\"");
            return true;
        }
        Player? client = FindPlayerByNameOrId(ctx.Arg(1));
        if (client is null) { ctx.Print($"stuffto: no such client '{ctx.Arg(1)}'."); return true; }
        if (StuffToClient is null) { ctx.Print("stuffto: no per-client command channel is wired on this host."); return true; }
        StuffToClient.Invoke(client, command);
        ctx.Print($"Command: \"{command}\" sent to {client.NetName} ({ctx.Arg(1)}).");
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // printstats — QC GameCommand_printstats → DumpStats(false).
    // ---------------------------------------------------------------------------------------------
    private bool CmdPrintStats(CommandContext ctx)
    {
        // The port's PlayerStats owns the full :player: event-log serialization; here we emit the live
        // roster snapshot (id/name/team/frags) so the verb is usable, and note the full dump path.
        ctx.Print("player stats:");
        foreach (Player p in _world.Clients.Players)
            ctx.Print($"  #{p.PlayerId} {p.NetName}  team={(int)p.Team}  frags={(int)p.Frags}");
        ctx.Print("stats dumped. (Full :player: event-log serialization lives in PlayerStats.)");
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Verbs whose behaviour depends on a subsystem the port doesn't expose yet: faithful usage +
    // honest note (T70 spec: no-op / "not implemented" is acceptable for these).
    // ---------------------------------------------------------------------------------------------
    private bool CmdDelRec(CommandContext ctx)
    {
        if (ctx.Arg(1) == "") { ctx.Print("Usage: delrec <ranking> [<map>]"); return true; }
        ctx.Print("delrec: not wired — RaceRecords has no rank-delete primitive yet (needs e.g. RaceRecords.DeleteUpToRank).");
        return true;
    }

    private bool CmdDatabase(CommandContext ctx)
    {
        ctx.Print("Usage: database <save|dump|load> <filename>");
        ctx.Print("  Not wired: the serverprogs key/value database (QC ServerProgsDB) is not part of the port.");
        return true;
    }

    private bool CmdMakeMapInfo(CommandContext ctx)
    {
        ctx.Print("make_mapinfo: not wired — mapinfo file (re)generation is an asset/menu-backend task, not ported here.");
        return true;
    }

    private bool CmdRadarMap(CommandContext ctx)
    {
        ctx.Print("radarmap: not implemented — radar image generation is a host rendering/asset task.");
        return true;
    }

    private bool CmdGetTagInfo(CommandContext ctx)
    {
        ctx.Print("Usage: gettaginfo <model> <frame> <index> [<cmd1>] [<cmd2>]");
        ctx.Print("  Not implemented — model tag/skeleton queries are a host (Godot) rendering concern.");
        return true;
    }

    private bool CmdEffectIndexDump(CommandContext ctx)
    {
        ctx.Print("effectindexdump: not implemented — the particle effect index table is client/asset-side (effectinfo.txt).");
        return true;
    }

    // ---- small local formatting/parse helpers (Adm-prefixed to avoid clashes in the partial class) ----
    private static string AdminCallerName(CommandContext ctx) => ctx.Caller?.NetName ?? "SERVER ADMIN";
    private static string AdmConsoleSafe(string s) => s.Replace("\"", "'").Replace("\n", " ");
    private static string AdmFmt(Vector3 v) =>
        $"{v.X.ToString("0.##", CultureInfo.InvariantCulture)} {v.Y.ToString("0.##", CultureInfo.InvariantCulture)} {v.Z.ToString("0.##", CultureInfo.InvariantCulture)}";

    private static bool AdmTryParseVec(string s, out Vector3 v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string[] p = s.Trim().Trim('\'', '"').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 3) return false;
        if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
        if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
        if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
        v = new Vector3(x, y, z);
        return true;
    }
}
