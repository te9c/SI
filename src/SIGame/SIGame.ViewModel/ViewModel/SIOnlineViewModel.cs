﻿using Microsoft.Extensions.Logging;
using SI.GameServer.Client;
using SIContentService.Client;
using SIContentService.Contract;
using SICore;
using SICore.Network;
using SICore.Network.Clients;
using SICore.Network.Configuration;
using SICore.Network.Servers;
using SIData;
using SIGame.ViewModel.Implementation;
using SIGame.ViewModel.Models;
using SIGame.ViewModel.PackageSources;
using SIGame.ViewModel.Properties;
using SIUI.ViewModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using Utils;

namespace SIGame.ViewModel;

/// <summary>
/// Provides a view model for game server lobby.
/// </summary>
public sealed class SIOnlineViewModel : ConnectionDataViewModel
{
    /// <summary>
    /// Random package marker.
    /// </summary>
    private const string RandomIndicator = "@{random}";

    private const int SupportedProtocolVersion = 1;

    public const int MaxAvatarSizeMb = 2;

    private SI.GameServer.Contract.HostInfo? _gamesHostInfo;

    public string ServerName => _gamesHostInfo?.Name ?? "SIGame";

    public string? ServerLicense => _gamesHostInfo?.License;

    protected override long? MaxPackageSize => _gamesHostInfo?.MaxPackageSizeMb;

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private GameInfo? _currentGame = null;

    public GameInfo? CurrentGame
    {
        get => _currentGame;
        set
        {
            if (_currentGame != value)
            {
                _currentGame = value;
                OnPropertyChanged();

                if (value != null)
                {
                    UpdateJoinCommand(value.Persons);
                }

                CheckJoin();
            }
        }
    }

    private bool _canJoin;

    public bool CanJoin
    {
        get => _canJoin;
        set
        {
            if (_canJoin != value)
            {
                _canJoin = value;
                OnPropertyChanged();
            }
        }
    }

    private void CheckJoin() =>
        CanJoin = _currentGame != null && (!_currentGame.PasswordRequired || !string.IsNullOrEmpty(_password));

    public CustomCommand Cancel { get; set; }

    public CustomCommand AddEmoji { get; set; }

    public GamesFilter GamesFilter
    {
        get => _userSettings.GamesFilter;
        set
        {
            if (_userSettings.GamesFilter != value)
            {
                _userSettings.GamesFilter = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(GamesFilterValue));

                lock (_serverGamesLock)
                {
                    RecountGames();
                }
            }
        }
    }

    public string GamesFilterValue
    {
        get
        {
            var value = "";
            var currentFilter = GamesFilter;

            var onlyNew = (currentFilter & GamesFilter.New) > 0;
            var sport = (currentFilter & GamesFilter.Sport) > 0;
            var tv = (currentFilter & GamesFilter.Tv) > 0;
            var noPassword = (currentFilter & GamesFilter.NoPassword) > 0;

            if ((sport && tv || !sport && !tv) && !onlyNew && !noPassword)
            {
                value = Resources.GamesFilter_All;
            }
            else
            {
                if (onlyNew)
                {
                    value += Resources.GamesFilter_New;
                }

                if (sport && !tv)
                {
                    if (value.Length > 0)
                    {
                        value += ", ";
                    }

                    value += Resources.GamesFilter_Sport;
                }

                if (tv && !sport)
                {
                    if (value.Length > 0)
                    {
                        value += ", ";
                    }

                    value += Resources.GamesFilter_Tv;
                }

                if (noPassword)
                {
                    if (value.Length > 0)
                    {
                        value += ", ";
                    }

                    value += Resources.GamesFilter_NoPassword;
                }
            }

            return value;
        }
    }

    public bool IsNew
    {
        get => (GamesFilter & GamesFilter.New) > 0;
        set
        {
            if (value)
            {
                GamesFilter |= GamesFilter.New;
            }
            else
            {
                GamesFilter &= ~GamesFilter.New;
            }
        }
    }

    public bool IsSport
    {
        get => (GamesFilter & GamesFilter.Sport) > 0;
        set
        {
            if (value)
                GamesFilter |= GamesFilter.Sport;
            else
                GamesFilter &= ~GamesFilter.Sport;
        }
    }

    public bool IsTv
    {
        get => (GamesFilter & GamesFilter.Tv) > 0;
        set
        {
            if (value)
                GamesFilter |= GamesFilter.Tv;
            else
                GamesFilter &= ~GamesFilter.Tv;
        }
    }

    public bool IsNoPassword
    {
        get => (GamesFilter & GamesFilter.NoPassword) > 0;
        set
        {
            if (value)
                GamesFilter |= GamesFilter.NoPassword;
            else
                GamesFilter &= ~GamesFilter.NoPassword;
        }
    }

    protected override bool IsOnline => true;

    public ObservableCollection<GameInfo> ServerGames { get; } = new ObservableCollection<GameInfo>();
    public List<GameInfo> ServerGamesCache { get; private set; } = new List<GameInfo>();

    private readonly object _serverGamesLock = new();

    private string _password = "";

    public string Password
    {
        get => _password;
        set
        {
            if (_password != value)
            {
                _password = value;
                OnPropertyChanged();
                CheckJoin();
            }
        }
    }

    private bool _showSearchBox;

    public bool ShowSearchBox
    {
        get => _showSearchBox;
        set
        {
            if (_showSearchBox != value)
            {
                _showSearchBox = value;
                OnPropertyChanged();

                if (!_showSearchBox)
                {
                    SearchFilter = "";
                }
            }
        }
    }

    private string _searchFilter = "";

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (_searchFilter != value)
            {
                _searchFilter = value;
                OnPropertyChanged();

                lock (_serverGamesLock)
                {
                    RecountGames();
                }
            }
        }
    }

    private bool _showProgress;

    public bool ShowProgress
    {
        get => _showProgress;
        set
        {
            if (_showProgress != value)
            {
                _showProgress = value;
                OnPropertyChanged();
            }
        }
    }

    private int _uploadProgress;

    public int UploadProgress
    {
        get => _uploadProgress;
        set
        {
            if (_uploadProgress != value)
            {
                _uploadProgress = value;
                OnPropertyChanged();
            }
        }
    }

    public string[] Emoji { get; } =
        new string[] { "😃", "😁", "😪", "🎄", "🎓", "💥", "🦄", "🍋", "🍄", "🔥", "❤️", "✨", "🎅", "🎁", "☃️", "🦌" };

    private string _chatText;

    public string ChatText
    {
        get => _chatText;
        set
        {
            if (_chatText != value)
            {
                _chatText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsChatShown
    {
        get => _userSettings.GameSettings.AppSettings.IsChatShown;
        set { _userSettings.GameSettings.AppSettings.IsChatShown = value; OnPropertyChanged(); }
    }

    protected override string[] ContentPublicBaseUrls => _gamesHostInfo?.ContentPublicBaseUrls ?? Array.Empty<string>();

    private readonly IGameServerClient _gameServerClient;

    public ObservableCollection<string> Users { get; } = new();

    private readonly object _usersLock = new();

    private readonly SIContentClientOptions? _defaultSIContentClientOptions;
    private readonly ILogger<SIOnlineViewModel> _logger;

    public SIOnlineViewModel(
        ConnectionData connectionData,
        IGameServerClient gameServerClient,
        CommonSettings commonSettings,
        UserSettings userSettings,
        SettingsViewModel settingsViewModel,
        SIContentClientOptions siContentClientOptions,
        ILogger<SIOnlineViewModel> logger)
        : base(connectionData, commonSettings, userSettings, settingsViewModel)
    {
        _gameServerClient = gameServerClient;

        _gameServerClient.GameCreated += GameServerClient_GameCreated;
        _gameServerClient.GameDeleted += GameServerClient_GameDeleted;
        _gameServerClient.GameChanged += GameServerClient_GameChanged;

        _gameServerClient.Joined += GameServerClient_Joined;
        _gameServerClient.Leaved += GameServerClient_Leaved;
        _gameServerClient.Receieve += OnMessage;

        _gameServerClient.Reconnecting += GameServerClient_Reconnecting;
        _gameServerClient.Reconnected += GameServerClient_Reconnected;
        _gameServerClient.Closed += GameServerClient_Closed;

        ServerAddress = _gameServerClient.ServiceUri;

        AddEmoji = new CustomCommand(AddEmoji_Executed);

        _defaultSIContentClientOptions = siContentClientOptions.ServiceUri != null ? siContentClientOptions : null;
        _logger = logger;

        NewGame.CanBeExecuted = false;
    }

    private Task GameServerClient_Reconnecting(Exception? exc)
    {
        OnMessage(Resources.App_Name, $"{Resources.ReconnectingMessage} {exc?.Message}");

        return Task.CompletedTask;
    }

    private Task GameServerClient_Reconnected(string? message)
    {
        OnMessage(Resources.App_Name, Resources.ReconnectedMessage);

        var cancellationToken = _cancellationTokenSource.Token;

        UI.Execute(
            async () =>
            {
                IsProgress = true;

                try
                {
                    await ReloadGamesAsync(cancellationToken);
                    await ReloadUsersAsync(cancellationToken);
                }
                finally
                {
                    IsProgress = false;
                }
            },
            exc =>
            {
                Error = exc.Message;
            },
            cancellationToken);

        return Task.CompletedTask;
    }

    private void AddEmoji_Executed(object? arg) => ChatText += arg.ToString();

    private Task GameServerClient_Closed(Exception? exception)
    {
        PlatformSpecific.PlatformManager.Instance.ShowMessage(
            $"{Resources.LostConnection}: {exception?.Message}",
            PlatformSpecific.MessageType.Warning);

        Cancel.Execute(null);

        return Task.CompletedTask;
    }

    public event Action<string, string> Message;

    private void OnMessage(string userName, string message) => Message?.Invoke(userName, message);

    private void GameServerClient_Leaved(string userName)
    {
        lock (_usersLock)
        {
            Users.Remove(userName);
        }
    }

    private void GameServerClient_Joined(string userName)
    {
        lock (_usersLock)
        {
            var inserted = false;

            var length = Users.Count;
            for (int i = 0; i < length; i++)
            {
                var comparison = Users[i].CompareTo(userName);
                if (comparison == 0)
                {
                    inserted = true;
                    break;
                }

                if (comparison > 0)
                {
                    Users.Insert(i, userName);
                    inserted = true;
                    break;
                }                        
            }

            if (!inserted)
                Users.Add(userName);
        }
    }

    private void GameServerClient_GameChanged(SI.GameServer.Contract.GameInfo gameInfo)
    {
        lock (_serverGamesLock)
        {
            for (int i = 0; i < ServerGamesCache.Count; i++)
            {
                if (ServerGamesCache[i].GameID == gameInfo.GameID)
                {
                    ServerGamesCache[i] = ToSICoreGame(gameInfo);
                    break;
                }
            }

            RecountGames();
        }
    }

    private void GameServerClient_GameDeleted(int id)
    {
        lock (_serverGamesLock)
        {
            for (int i = 0; i < ServerGamesCache.Count; i++)
            {
                if (ServerGamesCache[i].GameID == id)
                {
                    ServerGamesCache.RemoveAt(i);
                    break;
                }
            }

            RecountGames();
        }
    }

    private bool FilteredOk(GameInfo game) =>
        string.IsNullOrWhiteSpace(SearchFilter)
        || !SearchFilter.StartsWith(CommonSettings.OnlineGameUrl)
            && CultureInfo.CurrentUICulture.CompareInfo.IndexOf(game.GameName, SearchFilter.Trim(), CompareOptions.IgnoreCase) >= 0
        || SearchFilter.StartsWith(CommonSettings.OnlineGameUrl)
            && int.TryParse(SearchFilter.AsSpan(CommonSettings.OnlineGameUrl.Length), out int gameId)
            && game.GameID == gameId
        || SearchFilter.StartsWith(CommonSettings.NewOnlineGameUrl)
            && int.TryParse(SearchFilter.AsSpan(CommonSettings.NewOnlineGameUrl.Length), out int gameId2)
            && game.GameID == gameId2;

    private bool FilterGame(GameInfo gameInfo)
    {
        if ((GamesFilter & GamesFilter.New) > 0 && gameInfo.RealStartTime != DateTime.MinValue)
        {
            return false;
        }

        if ((GamesFilter & GamesFilter.Sport) == 0 && (GamesFilter & GamesFilter.Tv) > 0 && gameInfo.Mode == GameModes.Sport)
        {
            return false;
        }

        if ((GamesFilter & GamesFilter.Sport) > 0 && (GamesFilter & GamesFilter.Tv) == 0 && gameInfo.Mode == GameModes.Tv)
        {
            return false;
        }

        if ((GamesFilter & GamesFilter.NoPassword) > 0 && gameInfo.PasswordRequired)
        {
            return false;
        }

        if (!FilteredOk(gameInfo))
        {
            return false;
        }

        return true;
    }

    private void GameServerClient_GameCreated(SI.GameServer.Contract.GameInfo gameInfo)
    {
        lock (_serverGamesLock)
        {
            ServerGamesCache.Add(ToSICoreGame(gameInfo));
            RecountGames();
        }
    }

    private void InsertGame(GameInfo gameInfo)
    {
        var gameName = gameInfo.GameName;
        var length = ServerGames.Count;

        var inserted = false;

        for (int i = 0; i < length; i++)
        {
            var comparison = ServerGames[i].GameName.CompareTo(gameName);

            if (comparison == 0)
            {
                inserted = true;
                break;
            }

            if (comparison > 0)
            {
                ServerGames.Insert(i, gameInfo);
                inserted = true;
                break;
            }
        }

        if (!inserted)
            ServerGames.Add(gameInfo);
    }

    public async Task InitAsync()
    {
        IsProgress = true;

        try
        {
            _gamesHostInfo = await _gameServerClient.GetGamesHostInfoAsync(_cancellationTokenSource.Token);

            if (_gamesHostInfo.MaxPackageSizeMb == 0)
            {
                // For backward compatibility
                _gamesHostInfo.MaxPackageSizeMb = 100;
            }

            OnPropertyChanged(nameof(ServerName));

            await _gameServerClient.JoinLobbyAsync(_cancellationTokenSource.Token);

            await ReloadGamesAsync(_cancellationTokenSource.Token);
            await ReloadUsersAsync(_cancellationTokenSource.Token);

            _avatarLoadingTask = UploadUserAvatarAsync();

            NewGame.CanBeExecuted = true;
        }
        catch (TaskCanceledException)
        {

        }
        catch (ObjectDisposedException)
        {

        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.ShowMessage(exc.ToString(), PlatformSpecific.MessageType.Warning, true);                
            Cancel.Execute(null);
        }
        finally
        {
            IsProgress = false;
        }
    }

    private async Task<(string? AvatarUrl, FileKey? FileKey)> UploadUserAvatarAsync()
    {
        using var contentServiceClient = _gamesHostInfo?.ContentInfos?.Length > 0 ? GetContentClient() : null;
        return await UploadAvatarAsync(contentServiceClient, Human, _cancellationTokenSource.Token);
    }

    public async void Load()
    {
        try
        {
            var news = await _gameServerClient.GetNewsAsync(_cancellationTokenSource.Token);

            if (!string.IsNullOrEmpty(news))
            {
                OnMessage(Resources.News, news);
            }
        }
        catch (Exception exc)
        {
            Error = exc.Message;
            FullError = exc.ToString();
        }
    }

    private async Task ReloadUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var users = await _gameServerClient.GetUsersAsync(cancellationToken);
            Array.Sort(users);

            lock (_usersLock)
            {
                Users.Clear();

                foreach (var user in users)
                {
                    Users.Add(user);
                }
            }
        }
        catch (Exception exc)
        {
            Error = exc.Message;
            FullError = exc.ToString();
        }
    }

    protected override async Task ClearConnectionAsync()
    {
        if (ReleaseConnection)
        {
            await _gameServerClient.DisposeAsync();
        }
        else
        {
            _gameServerClient.GameCreated -= GameServerClient_GameCreated;
            _gameServerClient.GameDeleted -= GameServerClient_GameDeleted;
            _gameServerClient.GameChanged -= GameServerClient_GameChanged;

            _gameServerClient.Joined -= GameServerClient_Joined;
            _gameServerClient.Leaved -= GameServerClient_Leaved;
            _gameServerClient.Receieve -= OnMessage;

            _gameServerClient.Reconnecting -= GameServerClient_Reconnecting;
            _gameServerClient.Reconnected -= GameServerClient_Reconnected;
            _gameServerClient.Closed -= GameServerClient_Closed;
        }

        await base.ClearConnectionAsync();
    }

    private async Task ReloadGamesAsync(CancellationToken cancellationToken = default)
    {
        Error = "";

        try
        {
            lock (_serverGamesLock)
            {
                ServerGamesCache.Clear();
                RecountGames();
            }

            SI.GameServer.Contract.Slice<SI.GameServer.Contract.GameInfo> gamesSlice = null;
            var whileGuard = 100;

            do
            {
                var fromId = gamesSlice != null && gamesSlice.Data.Length > 0 ? gamesSlice.Data.Last().GameID + 1 : 0;
                gamesSlice = await _gameServerClient.GetGamesAsync(fromId, cancellationToken);

                lock (_serverGamesLock)
                {
                    ServerGamesCache.AddRange(gamesSlice.Data.Select(ToSICoreGame));
                    RecountGames();
                }

                whileGuard--;
            } while (!gamesSlice.IsLastSlice && whileGuard > 0);
        }
        catch (Exception exc)
        {
            Error = exc.Message;
            FullError = exc.ToString();
        }
    }

    private void RecountGames()
    {
        var serverGames = ServerGames.ToArray();

        for (var i = 0; i < serverGames.Length; i++)
        {
            var item = serverGames[i];
            var game = ServerGamesCache.FirstOrDefault(sg => sg.GameID == item.GameID);

            if (game == null || !FilterGame(game))
            {
                ServerGames.Remove(item);
            }
        }

        serverGames = ServerGames.ToArray();

        for (var i = 0; i < serverGames.Length; i++)
        {
            var item = serverGames[i];
            var game = ServerGamesCache.FirstOrDefault(sg => sg.GameID == item.GameID);

            if (game != null && game != item)
            {
                ServerGames[i] = game;
            }
        }

        for (int i = 0; i < ServerGamesCache.Count; i++)
        {
            var item = ServerGamesCache[i];

            var game = ServerGames.FirstOrDefault(sg => sg.GameID == item.GameID);

            if (game == null && FilterGame(item))
            {
                InsertGame(item);
            }
        }

        if (CurrentGame != null && !ServerGames.Contains(CurrentGame))
            CurrentGame = null;

        if (CurrentGame == null && ServerGames.Any())
            CurrentGame = ServerGames[0];
    }

    protected override void Prepare(GameSettingsViewModel gameSettings)
    {
        base.Prepare(gameSettings);

        gameSettings.NetworkGameType = NetworkGameType.GameServer;
        gameSettings.CreateGame += CreateGameAsync;
    }

    private async Task<(SecondaryNode, IViewerClient)?> CreateGameAsync(GameSettings settings, PackageSource packageSource)
    {
        GameSettings.Message = Resources.PackageCheck;

        var cancellationTokenSource = GameSettings.CancellationTokenSource = new CancellationTokenSource();

        var hash = await packageSource.GetPackageHashAsync(cancellationTokenSource.Token);

        var packageKey = new PackageKey
        {
            Name = packageSource.GetPackageName(),
            Hash = hash,
            Uri = packageSource.GetPackageUri()
        };

        return await CreateGameWithContentServiceAsync(settings, packageKey, packageSource, cancellationTokenSource.Token);
    }

    private async Task<(SecondaryNode, IViewerClient)?> CreateGameWithContentServiceAsync(
        GameSettings settings,
        PackageKey packageKey,
        PackageSource packageSource,
        CancellationToken cancellationToken)
    {
        using var contentServiceClient = GetContentClient();

        SI.GameServer.Contract.PackageInfo packageInfo;

        if (IsCustomPackage(packageKey))
        {
            var customPackageInfo = await ProcessCustomPackageAsync(packageKey, packageSource, contentServiceClient, cancellationToken);

            if (customPackageInfo == null)
            {
                return null;
            }

            packageInfo = customPackageInfo;
        }
        else // Library package
        {
            if (packageKey.Uri == null) // Random package
            {
                return null;
            }

            packageInfo = new SI.GameServer.Contract.PackageInfo
            {
                Type = SI.GameServer.Contract.PackageType.LibraryItem,
                Uri = packageKey.Uri,
            };
        }

        GameSettings.Message = Resources.Preparing;

        var computerAccounts = await ProcessCustomPersonsAsync(contentServiceClient, settings, cancellationToken);

        GameSettings.Message = Resources.Creating;

        settings.AppSettings.Culture = Thread.CurrentThread.CurrentUICulture.Name;

        var runGameResponse = await _gameServerClient.RunGameAsync(
            new SI.GameServer.Contract.RunGameRequest((
                GameSettingsCore<AppSettingsCore>)settings,
                packageInfo,
                computerAccounts.Select(ca => ca.Account).ToArray()),
            cancellationToken);

        if (!runGameResponse.IsSuccess)
        {
            throw new Exception(GetMessage(runGameResponse.ErrorType));
        }

        GameSettings.Message = Resources.GameEntering;

        var name = Human.Name;

        _password = settings.NetworkGamePassword;

        var game = new GameInfo
        {
            HostUri = runGameResponse.HostUri,
            GameID = runGameResponse.GameId,
            Owner = name
        };

        await JoinGameAsync(game, settings.Role, runGameResponse.IsHost, cancellationToken);

        if (_host == null)
        {
            return null;
        }

        _host.Connector?.SetGameID(runGameResponse.GameId);

        return (_node, _host);
    }

    private static bool IsCustomPackage(PackageKey packageKey) => packageKey.Hash.Length > 0; // Does not look nice, but will work for now

    private async Task<SI.GameServer.Contract.PackageInfo?> ProcessCustomPackageAsync(
        PackageKey packageKey,
        PackageSource packageSource,
        ISIContentServiceClient contentServiceClient,
        CancellationToken cancellationToken)
    {
        var key = new SIContentService.Contract.Models.FileKey
        {
            Name = packageKey.Name,
            Hash = packageKey.Hash
        };

        var packageUri = await contentServiceClient.TryGetPackageUriAsync(key, cancellationToken);

        if (packageUri == null)
        {
            var packageStream = await packageSource.GetPackageDataAsync(cancellationToken) ?? throw new Exception(Resources.BadPackage);

            using (packageStream)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (packageStream.Length > _gamesHostInfo?.MaxPackageSizeMb * 1024 * 1024)
                {
                    throw new Exception($"{Resources.FileTooLarge}. {string.Format(Resources.MaximumFileSize, _gamesHostInfo?.MaxPackageSizeMb)}");
                }

                GameSettings.Message = Resources.SendingPackageToServer;
                UploadProgress = 0;
                ShowProgress = true;

                try
                {
                    packageUri = await contentServiceClient.UploadPackageAsync(key, packageStream, cancellationToken);
                }
                finally
                {
                    ShowProgress = false;
                }
            }
        }

        return new SI.GameServer.Contract.PackageInfo
        {
            Type = SI.GameServer.Contract.PackageType.Content,
            Uri = packageUri,
            ContentServiceUri = contentServiceClient.ServiceUri,
            Secret = _defaultSIContentClientOptions?.ClientSecret,
        };
    }

    private ISIContentServiceClient GetContentClient() =>
        SIContentClientExtensions.CreateSIContentServiceClient(
            _defaultSIContentClientOptions ?? new SIContentClientOptions
            {
                ServiceUri = GetContentInfo().ServiceUri,
            },
            progress => UploadProgress = progress);

    private SI.GameServer.Contract.SIContentInfo GetContentInfo()
    {
        var randomIndex = Random.Shared.Next(_gamesHostInfo!.ContentInfos.Length);
        return _gamesHostInfo.ContentInfos[randomIndex];
    }

    private async Task InitNodeAndClientNewAsync(IGameClient gameClient, CancellationToken cancellationToken = default)
    {
        _node = new GameServerSlave(
            NodeConfiguration.Default,
            new NetworkLocalizer(Thread.CurrentThread.CurrentUICulture.Name));

        await _node.AddConnectionAsync(new GameServerConnection(gameClient) { IsAuthenticated = true }, cancellationToken);

        _client = new Client(Human.Name);
        _client.ConnectTo(_node);
    }

    private static string GetMessage(SI.GameServer.Contract.GameCreationResultCode gameCreationResultCode) =>
        gameCreationResultCode switch
        {
            SI.GameServer.Contract.GameCreationResultCode.NoPackage => Resources.GameCreationError_NoPackage,
            SI.GameServer.Contract.GameCreationResultCode.TooMuchGames => Resources.GameCreationError_TooManyGames,
            SI.GameServer.Contract.GameCreationResultCode.ServerUnderMaintainance => Resources.GameCreationError_ServerMaintainance,
            SI.GameServer.Contract.GameCreationResultCode.BadPackage => Resources.GameCreationError_BadPackage,
            SI.GameServer.Contract.GameCreationResultCode.GameNameCollision => Resources.GameCreationError_DuplicateName,
            SI.GameServer.Contract.GameCreationResultCode.InternalServerError => Resources.GameCreationError_ServerError,
            SI.GameServer.Contract.GameCreationResultCode.ServerNotReady => Resources.GameCreationError_ServerNotReady,
            SI.GameServer.Contract.GameCreationResultCode.YourClientIsObsolete => Resources.GameCreationError_ObsoleteVersion,
            SI.GameServer.Contract.GameCreationResultCode.UnknownError => Resources.GameCreationError_UnknownReason,
            SI.GameServer.Contract.GameCreationResultCode.JoinError => Resources.GameCreationError_JoinError,
            SI.GameServer.Contract.GameCreationResultCode.WrongGameSettings => Resources.GameCreationError_WrongSettings,
            SI.GameServer.Contract.GameCreationResultCode.TooManyGamesByAddress => Resources.TooManyGames,
            _ => Resources.GameCreationError_UnknownReason,
        };

    private async Task<List<ComputerAccountInfo>> ProcessCustomPersonsAsync(
        ISIContentServiceClient? contentServiceClient,
        GameSettings settings,
        CancellationToken cancellationToken)
    {
        var computerAccounts = new List<ComputerAccountInfo>();

        foreach (var player in settings.Players)
        {
            await ProcessCustomPersonAsync(contentServiceClient, computerAccounts, player, cancellationToken);
        }

        await ProcessCustomPersonAsync(contentServiceClient, computerAccounts, settings.Showman, cancellationToken);

        return computerAccounts;
    }

    private async Task ProcessCustomPersonAsync(
        ISIContentServiceClient? contentServiceClient,
        List<ComputerAccountInfo> computerAccounts,
        Account account,
        CancellationToken cancellationToken)
    {
        if (!account.IsHuman && account.CanBeDeleted) // This is a non standard player; we need to send its data to server
        {
            var avatar = (await UploadAvatarAsync(contentServiceClient, account, cancellationToken)).AvatarUrl;
            var computerAccount = new ComputerAccount((ComputerAccount)account) { Picture = avatar ?? "" };
            computerAccounts.Add(new ComputerAccountInfo { Account = computerAccount });
        }
    }

    private async Task<(string? AvatarUrl, FileKey? FileKey)> UploadAvatarAsync(
        ISIContentServiceClient? contentServiceClient,
        Account account,
        CancellationToken cancellationToken = default)
    {
        var avatarUri = account.Picture;

        if (!Uri.TryCreate(avatarUri, UriKind.Absolute, out var pictureUri))
        {
            return (null, null);
        }

        if (pictureUri.Scheme != "file" || !File.Exists(avatarUri))
        {
            return (null, null);
        }

        // This is a local file and it should be sent to server
        if (new FileInfo(avatarUri).Length > MaxAvatarSizeMb * 1024 * 1024)
        {
            return (null, null);
        }

        byte[] fileHash;

        using (var stream = File.OpenRead(avatarUri))
        {
            using var sha1 = SHA1.Create();
            fileHash = sha1.ComputeHash(stream);
        }

        var avatarKey = new FileKey { Name = Path.GetFileName(avatarUri), Hash = fileHash };

        if (contentServiceClient?.ServiceUri == null)
        {
            throw new InvalidOperationException("contentServiceClient?.ServiceUri == null");
        }

        var key = new SIContentService.Contract.Models.FileKey
        {
            Name = Path.GetFileName(avatarUri),
            Hash = fileHash,
        };

        var avatarServerUri = await contentServiceClient.TryGetAvatarUriAsync(key, cancellationToken);

        if (avatarServerUri == null)
        {
            using var stream = File.OpenRead(avatarUri);
            avatarServerUri = await contentServiceClient.UploadAvatarAsync(key, stream, cancellationToken);
        }

        if (avatarServerUri != null && !avatarServerUri.IsAbsoluteUri)
        {
            // Prepend avatarServerUri with service content root uri
            avatarServerUri = new Uri(contentServiceClient.ServiceUri, avatarServerUri.ToString().TrimStart('/'));
        }

        return (avatarServerUri?.ToString(), avatarKey);
    }

    protected override string GetExtraCredentials() => !string.IsNullOrEmpty(_password) ? $"\n{_password}" : "";

    public override async Task JoinGameCoreAsync(
        GameInfo? gameInfo,
        GameRole role,
        bool isHost = false,
        CancellationToken cancellationToken = default)
    {
        gameInfo ??= _currentGame;

        if (gameInfo != null && gameInfo.MinimumClientProtocolVersion > SupportedProtocolVersion)
        {
            Error = Resources.YouNeedToUpgradeClientToJoinGame;
            return;
        }

        if (gameInfo == null)
        {
            return;
        }

        if (!isHost)
        {
            lock (_serverGamesLock)
            {
                var passwordRequired = gameInfo.PasswordRequired;

                if (passwordRequired && string.IsNullOrEmpty(_password))
                {
                    IsProgress = false;
                    return;
                }
            }
        }

        try
        {
            _logger.LogInformation("Joining game: UseSignalRConnection = {useSignalRConnection}", _userSettings.UseSignalRConnection);

            if (_userSettings.UseSignalRConnection)
            {
                var siHostClient = await SIHostClient.CreateAsync(
                    new SIHostClientOptions { ServiceUri = gameInfo.HostUri },
                    cancellationToken);

                var result = await siHostClient.JoinGameAsync(
                    new SI.GameServer.Contract.JoinGameRequest(
                        gameInfo.GameID,
                        Human.Name,
                        role,
                        Human.IsMale ? SI.GameServer.Contract.Sex.Male : SI.GameServer.Contract.Sex.Female,
                        _password),
                    cancellationToken);

                if (!result.IsSuccess)
                {
                    Error = result.ErrorType switch
                    {
                        SI.GameServer.Contract.JoinGameErrorType.GameNotFound => Resources.GameNotFound,
                        SI.GameServer.Contract.JoinGameErrorType.CommonJoinError => Resources.CommonJoinError,
                        SI.GameServer.Contract.JoinGameErrorType.InvalidRole => Resources.InvalidRole,
                        SI.GameServer.Contract.JoinGameErrorType.InternalServerError => Resources.InternalServerError,
                        SI.GameServer.Contract.JoinGameErrorType.Forbidden => Resources.JoinForbidden,
                        _ => Resources.UnknownError
                    } + ' ' + result.Message;

                    await siHostClient.DisposeAsync();

                    return;
                }

                await InitNodeAndClientNewAsync(siHostClient, cancellationToken);
                await JoinGameCompletedAsync(role, isHost, cancellationToken);

                await _gameServerClient.DisposeAsync();
            }
            else
            {
                await InitServerAndClientAsync(_gamesHostInfo.Host ?? new Uri(ServerAddress).Host, _gamesHostInfo.Port);
                await ConnectCoreAsync(true);

                var result = await _connector.SetGameIdAsync(gameInfo.GameID);

                if (!result)
                {
                    Error = Resources.CreatedGameNotFound;
                    return;
                }

                await base.JoinGameCoreAsync(gameInfo, role, isHost, cancellationToken);
            }

            _host.Connector.SetGameID(gameInfo.GameID);
        }
        catch (TaskCanceledException exc)
        {
            Error = Resources.GameConnectionTimeout;
            FullError = exc.ToString();
        }
        catch (Exception exc)
        {
            Error = exc.Message;
            FullError = exc.ToString();
        }
    }

    public void Say(string message, bool system = false)
    {
        if (!system)
        {
            _gameServerClient.SayAsync(message);
        }
    }

    protected override void CloseContent_Executed(object? arg)
    {
        GameSettings?.CancellationTokenSource?.Cancel();
        base.CloseContent_Executed(arg);
    }

    private static GameInfo ToSICoreGame(SI.GameServer.Contract.GameInfo gameInfo) =>
        new()
        {
            GameID = gameInfo.GameID,
            GameName = gameInfo.GameName,
            Mode = gameInfo.Mode,
            Owner = gameInfo.Owner,
            PackageName = gameInfo.PackageName == RandomIndicator ? Resources.RandomServerThemes : gameInfo.PackageName,
            PasswordRequired = gameInfo.PasswordRequired,
            Persons = gameInfo.Persons,
            RealStartTime = gameInfo.RealStartTime == DateTime.MinValue ? DateTime.MinValue : gameInfo.RealStartTime.ToLocalTime(),
            Rules = BuildRules(gameInfo),
            Stage = BuildStage(gameInfo),
            Started = gameInfo.Started,
            StartTime = gameInfo.StartTime.ToLocalTime(),
            MinimumClientProtocolVersion = gameInfo.MinimumClientProtocolVersion,
            HostUri = gameInfo.HostUri,
        };

    private static string BuildStage(SI.GameServer.Contract.GameInfo gameInfo) => gameInfo.Stage switch
    {
        GameStages.Created => Resources.GameStage_Created,
        GameStages.Started => Resources.GameStage_Started,
        GameStages.Round => $"{gameInfo.ProgressCurrent}/{gameInfo.ProgressTotal}: {gameInfo.StageName}",
        GameStages.Final => Resources.GameStage_Final,
        _ => Resources.GameStage_Finished,
    };

    private static string[] BuildRules(SI.GameServer.Contract.GameInfo gameInfo)
    {
        var rules = gameInfo.Rules;
        var ruleValues = new List<string>();

        if (gameInfo.Mode == GameModes.Sport)
        {
            ruleValues.Add(Resources.GameRule_Sport);
        }
        else if (gameInfo.Mode == GameModes.Sport)
        {
            ruleValues.Add(Resources.GameRule_Classic);
        }

        if ((rules & SI.GameServer.Contract.GameRules.FalseStart) == 0)
        {
            ruleValues.Add(Resources.GameRule_NoFalseStart);
        }

        if ((rules & SI.GameServer.Contract.GameRules.Oral) > 0)
        {
            ruleValues.Add(Resources.GameRule_Oral);
        }

        if ((rules & SI.GameServer.Contract.GameRules.IgnoreWrong) > 0)
        {
            ruleValues.Add(Resources.GameRule_IgnoreWrong);
        }

        if ((rules & SI.GameServer.Contract.GameRules.PingPenalty) > 0)
        {
            ruleValues.Add(Resources.GameRule_PingPenalty);
        }

        return ruleValues.ToArray();
    }

    public override ValueTask DisposeAsync()
    {
        GameSettings?.CancellationTokenSource?.Cancel();

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        return base.DisposeAsync();
    }
}
