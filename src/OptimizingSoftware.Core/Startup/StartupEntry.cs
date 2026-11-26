namespace OptimizingSoftware.Core.Startup;

/// <summary>Endroit d'où Windows lance un programme à l'ouverture de session.</summary>
public enum StartupLocation
{
    /// <summary>HKCU\...\Run : pour ton compte.</summary>
    RunUser,
    /// <summary>HKLM\...\Run : pour tout le PC (64 bits).</summary>
    RunMachine,
    /// <summary>HKLM\WOW6432Node\...\Run : pour tout le PC (32 bits).</summary>
    RunMachine32,
    /// <summary>Dossier Démarrage de ton compte.</summary>
    FolderUser,
    /// <summary>Dossier Démarrage commun à tous les comptes.</summary>
    FolderCommon,
}

/// <summary>Un programme lancé au démarrage, tel que le liste le Gestionnaire des tâches.</summary>
/// <param name="Name">Nom de la valeur du registre ou du raccourci (sans .lnk).</param>
/// <param name="ExePath">Programme lancé, si on a pu le retrouver.</param>
/// <param name="Publisher">Éditeur lu dans le fichier (signature ou informations de version).</param>
/// <param name="Signed">Vrai si le programme a une signature numérique valide ; null si on n'a pas pu vérifier.</param>
public sealed record StartupEntry(
    string Name,
    string Command,
    string? ExePath,
    StartupLocation Location,
    bool Enabled,
    string? Publisher,
    bool? Signed,
    bool ExeExists)
{
    public bool ForAllUsers => Location is StartupLocation.RunMachine or StartupLocation.RunMachine32 or StartupLocation.FolderCommon;
}

/// <summary>
/// Valeurs « StartupApproved » qu'utilise le Gestionnaire des tâches pour activer ou désactiver un programme au
/// démarrage sans toucher à son entrée : 12 octets, le premier pair = activé, impair = désactivé.
/// </summary>
public static class StartupApproved
{
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] Enabled() => [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Désactivé, avec la date (comme le Gestionnaire des tâches, qui l'affiche).</summary>
    public static byte[] Disabled(DateTime nowUtc)
    {
        var value = new byte[12];
        value[0] = 3;
        BitConverter.GetBytes(nowUtc.ToFileTimeUtc()).CopyTo(value, 4);
        return value;
    }
}

public enum StartupAdvice { Keep, Disable, Review, Broken }

/// <summary>Conseil pour chaque programme au démarrage.</summary>
public static class StartupAdvisor
{
    /// <summary>Utiles au démarrage : sécurité, pilotes (son, carte graphique, touchpad), synchronisation.</summary>
    private static readonly string[] KeepKeys =
    [
        "securityhealth", "windowsdefender", "realtek", "nvidia", "nvcontainer", "nvbackend", "amd", "radeon", "intel",
        "igfx", "synaptics", "elan", "logitech", "lghub", "razer", "corsair", "steelseries", "onedrive", "dropbox",
        "googledrive", "malwarebytes", "avast", "avg", "kaspersky", "bitdefender", "eset", "norton", "mcafee",
        "keepass", "bitwarden", "1password", "ctfmon", "msiafterburner", "rtss",
    ];

    /// <summary>Lancés au démarrage par habitude alors qu'on les ouvre soi-même quand on en a besoin.</summary>
    private static readonly (string Key, string Why)[] DisableKeys =
    [
        ("steam", "Launcher de jeux : il s'ouvre tout seul quand tu lances un jeu Steam."),
        ("epicgames", "Launcher de jeux : ouvre-le quand tu veux jouer."),
        ("eadesktop", "Launcher de jeux : ouvre-le quand tu veux jouer."),
        ("origin", "Launcher de jeux : ouvre-le quand tu veux jouer."),
        ("ubisoftconnect", "Launcher de jeux : ouvre-le quand tu veux jouer."),
        ("upc", "Launcher de jeux (Ubisoft Connect) : ouvre-le quand tu veux jouer."),
        ("battlenet", "Launcher de jeux : ouvre-le quand tu veux jouer."),
        ("goggalaxy", "Launcher de jeux : ouvre-le quand tu veux jouer."),
        ("riotclient", "Launcher de jeux : ouvre-le quand tu veux jouer."),
        ("discord", "Messagerie : elle peut s'ouvrir quand tu en as besoin, et elle prend de la mémoire en arrière-plan."),
        ("spotify", "Lecteur de musique : ouvre-le quand tu veux écouter."),
        ("teams", "Messagerie : utile seulement si tu t'en sers tous les jours."),
        ("skype", "Messagerie : utile seulement si tu t'en sers tous les jours."),
        ("zoom", "Visio : elle s'ouvre toute seule quand tu cliques sur un lien de réunion."),
        ("opera", "Navigateur lancé au démarrage : inutile, il s'ouvre quand tu en as besoin."),
        ("operagx", "Navigateur lancé au démarrage : inutile, il s'ouvre quand tu en as besoin."),
        ("edgeautolaunch", "Microsoft Edge préchargé au démarrage : inutile si tu ne t'en sers pas."),
        ("msedge", "Microsoft Edge préchargé au démarrage : inutile si tu ne t'en sers pas."),
        ("chrome", "Navigateur lancé au démarrage : inutile, il s'ouvre quand tu en as besoin."),
        ("adobe", "Mises à jour et services Adobe : ils peuvent attendre l'ouverture d'un logiciel Adobe."),
        ("acrobat", "Mises à jour et services Adobe : ils peuvent attendre l'ouverture d'un logiciel Adobe."),
        ("ccleaner", "Nettoyeur qui tourne en permanence : inutile, cette appli fait le ménage à la demande."),
        ("cortana", "Assistant rarement utilisé."),
        ("itunes", "Assistant iTunes : inutile si tu ne branches pas d'iPhone tous les jours."),
        ("wallpaperengine", "Fond d'écran animé : il consomme de la carte graphique en permanence."),
        ("overwolf", "Overlay pour jeux : il ralentit le démarrage et les jeux."),
        ("medal", "Enregistreur de clips : il tourne en arrière-plan en permanence."),
        ("obs", "Logiciel d'enregistrement : ouvre-le quand tu veux enregistrer."),
        ("utorrent", "Client torrent : il partage des fichiers en arrière-plan."),
        ("bittorrent", "Client torrent : il partage des fichiers en arrière-plan."),
        ("qbittorrent", "Client torrent : il partage des fichiers en arrière-plan."),
    ];

    public static (StartupAdvice Advice, string Reason) Advise(StartupEntry entry)
    {
        if (entry.ExePath is not null && !entry.ExeExists)
            return (StartupAdvice.Broken, "Le programme n'existe plus : l'entrée ne sert à rien (Windows affiche parfois une erreur au démarrage).");

        string[] names =
        [
            Apps.NameMatcher.Normalize(entry.Name),
            Apps.NameMatcher.Normalize(Path.GetFileNameWithoutExtension(entry.ExePath ?? "")),
            Apps.NameMatcher.Normalize(entry.Command),
        ];
        foreach (var k in KeepKeys)
            if (Matches(names, k))
                return (StartupAdvice.Keep, "Utile au démarrage (sécurité, pilote, synchronisation ou périphérique).");
        foreach (var (k, why) in DisableKeys)
            if (Matches(names[..2], k))
                return (StartupAdvice.Disable, why);
        if (entry.Signed == false && IsUserWritable(entry.ExePath))
            return (StartupAdvice.Review, "Programme sans signature lancé depuis un dossier personnel ou temporaire : vérifie que tu le connais.");
        return (StartupAdvice.Review, "À toi de voir : désactive-le si tu ne t'en sers pas tous les jours. Tu pourras le réactiver ici.");
    }

    /// <summary>
    /// Les mots courts (« amd », « obs ») doivent former tout le nom, éventuellement suivi de chiffres (obs64),
    /// pour éviter les faux amis (Obsidian).
    /// </summary>
    private static bool Matches(IEnumerable<string> names, string key) =>
        names.Any(n => key.Length >= 5
            ? n.Contains(key, StringComparison.Ordinal)
            : n.StartsWith(key, StringComparison.Ordinal) && n[key.Length..].All(char.IsDigit));

    /// <summary>Dossiers où n'importe quel programme peut écrire sans droits administrateur.</summary>
    public static bool IsUserWritable(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.Replace('/', '\\');
        return p.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase)
               || p.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase)
               || p.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase)
               || p.Contains(@"\Téléchargements\", StringComparison.OrdinalIgnoreCase)
               || p.Contains(@"\Users\Public\", StringComparison.OrdinalIgnoreCase)
               || (p.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase) && p.Count(c => c == '\\') <= 2);
    }
}
