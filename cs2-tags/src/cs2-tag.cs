using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using TagsApi;
using static Tags.TagExtensions;
using static TagsApi.Tags;

namespace Tags;

public class Tags : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "Tags";
    public override string ModuleVersion => "1.15";
    public override string ModuleAuthor => "schwarper";

    public static readonly Dictionary<ulong, Tag> PlayerTagsList = [];
    public static readonly TagsAPI Api = new();
    public static Tags Instance { get; set; } = new();
    public Config Config { get; set; } = new();

    public override void Load(bool hotReload)
    {
        Instance = this;
        Capabilities.RegisterPluginCapability(ITagApi.Capability, () => Api);

        foreach (string command in Config.Commands.TagsReload)
            AddCommand(command, "Tags Reload", Command_Tags_Reload);

        foreach (string command in Config.Commands.Visibility)
            AddCommand(command, "Visibility", Command_Visibility);

        HookUserMessage(118, OnMessage, HookMode.Pre);
        AddCommandListener("css_admins_reload", Command_Admins_Reloads, HookMode.Pre);

        if (hotReload)
            ReloadTags();
    }

    public override void Unload(bool hotReload)
    {
        UnhookUserMessage(118, OnMessage, HookMode.Pre);
        RemoveCommandListener("css_admins_reload", Command_Admins_Reloads, HookMode.Pre);
    }

    public void OnConfigParsed(Config config)
    {
        config.Settings.Init();
        Config = config;
    }

    public static HookResult Command_Admins_Reloads(CCSPlayerController? player, CommandInfo info)
    {
        ReloadConfig();
        ReloadTags();
        return HookResult.Continue;
    }

    [RequiresPermissions("@css/root")]
    public static void Command_Tags_Reload(CCSPlayerController? player, CommandInfo info)
    {
        ReloadConfig();
        ReloadTags();
    }

    [RequiresPermissions("@css/admin")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void Command_Visibility(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return;
        }

        if (player.GetVisibility())
        {
            player.SetVisibility(false);
            info.ReplyToCommand(Config.Settings.Tag + Localizer.ForPlayer(player, "Tags are now hidden"));
        }
        else
        {
            player.SetVisibility(true);
            info.ReplyToCommand(Config.Settings.Tag + Localizer.ForPlayer(player, "Tags are now visible"));
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (@event.Userid is not CCSPlayerController player || player.IsBot)
            return HookResult.Continue;

        PlayerTagsList[player.SteamID] = player.GetTag();
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is not CCSPlayerController player || player.IsBot)
            return HookResult.Continue;

        PlayerTagsList.Remove(player.SteamID);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (@event.Userid is not { } player || player.IsBot)
            return HookResult.Continue;

        var tag = GetOrCreatePlayerTag(player, false);
        player.SetScoreTag(tag.ScoreTag);
        return HookResult.Continue;
    }

    public HookResult OnMessage(UserMessage um)
    {
        if (Utilities.GetPlayerFromIndex(um.ReadInt("entityindex")) is not CCSPlayerController player || player.IsBot)
            return HookResult.Continue;
        
        return HookResult.Handled;
    }
    
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        var userID = @event.Userid;
        var player = Utilities.GetPlayerFromUserid(userID);
        string message = @event.Text;

        if (player == null || !player.IsValid || player.IsBot || string.IsNullOrEmpty(message))
            return HookResult.Continue;

        var tag = GetOrCreatePlayerTag(player, false);

        MessageProcess messageProcess = new()
        {
            Player = player,
            Tag = !player.GetVisibility() ? Config.Default.Clone() : tag.Clone(),
            Message = message.RemoveCurlyBraceContent(),
            PlayerName = player.PlayerName,
            ChatSound = tag.ChatSound,
            TeamMessage = @event.Teamonly
        };
        
        Api.MessageProcessPre(messageProcess);

        string deadname = player.PawnIsAlive ? string.Empty : Config.Settings.DeadName;
        string teamname = messageProcess.TeamMessage ? player.Team.Name() : string.Empty;
        Tag playerData = messageProcess.Tag;
        CsTeam team = player.Team;

        messageProcess.PlayerName = FormatMessage(team, deadname, teamname, playerData.ChatTag ?? string.Empty, playerData.NameColor ?? string.Empty, messageProcess.PlayerName);
        messageProcess.Message = FormatMessage(team, playerData.ChatColor ?? string.Empty, messageProcess.Message);
        
        Api.MessageProcess(messageProcess);

        string finalFormattedString = $"{messageProcess.PlayerName}{ChatColors.White}: {messageProcess.Message}";
        
        if (messageProcess.TeamMessage)
        {
            var teamPlayers = Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot);
            foreach (var p in teamPlayers)
            {
                p.PrintToChat(finalFormattedString);
            }
        }
        else
        {
            Server.PrintToChatAll(finalFormattedString);
        }

        Api.MessageProcessPost(messageProcess);
        
        return HookResult.Continue;
    }
}