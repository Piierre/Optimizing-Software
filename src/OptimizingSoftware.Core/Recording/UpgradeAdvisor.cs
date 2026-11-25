using System.Text.RegularExpressions;
using OptimizingSoftware.Core.Diagnostics;

namespace OptimizingSoftware.Core.Recording;

/// <summary>Conseils d'achat selon ce qui limite, adaptés à la configuration quand on la connaît.</summary>
public static partial class UpgradeAdvisor
{
    public static IReadOnlyList<string> Advise(Limiter limiter, SystemSnapshot? pc)
    {
        var advice = new List<string>();
        var cpu = pc?.Cpu;
        var mem = pc?.Memory;
        var ramType = mem?.Modules.FirstOrDefault()?.Type is { } t and not MemoryType.Unknown
            ? t.ToString().ToUpperInvariant() : "";

        switch (limiter)
        {
            case Limiter.Cpu:
                if (cpu is not null && IntelCoffeeLake().IsMatch(cpu.Name))
                {
                    advice.Add("Option économique : un i7-9700K ou un i9-9900K d'occasion. Ils se montent sur ta carte mère actuelle sans rien changer d'autre (pense à mettre à jour le BIOS avant).");
                    advice.Add("Option durable : changer de plateforme (processeur + carte mère + RAM DDR5). Plus cher, mais tu repars pour plusieurs années et tu pourras changer de carte graphique plus tard.");
                }
                else
                {
                    advice.Add("Un processeur avec plus de cœurs et de threads. Vérifie qu'il est compatible avec le socket et le chipset de ta carte mère.");
                }
                advice.Add("En attendant : ferme les applis en arrière-plan (navigateur, Discord, OBS) et active le mode Jeu de Windows.");
                break;

            case Limiter.Gpu:
                advice.Add("Une carte graphique plus récente. C'est l'achat qui fait gagner le plus de FPS.");
                if (cpu is { LogicalProcessors: < 8 })
                    advice.Add($"Attention : avec ton processeur ({cpu.LogicalProcessors} threads), une carte très puissante ne sera pas exploitée à fond. Vise le milieu de gamme.");
                advice.Add("Gratuit : baisse la résolution de rendu ou active DLSS / FSR si le jeu le propose.");
                break;

            case Limiter.Ram:
                var total = mem is null ? 0 : mem.TotalBytes / 1024d / 1024 / 1024;
                advice.Add(total is > 0 and < 30
                    ? $"Passe à 32 Go de RAM {ramType} (2 barrettes de 16 Go identiques). C'est peu coûteux."
                    : "Ferme les applications gourmandes pendant que tu joues (navigateur, Discord, OBS).");
                break;

            case Limiter.Disk:
                var systemDisk = pc?.Disks.FirstOrDefault(d => d.IsSystemDrive);
                advice.Add(systemDisk?.MediaType == DiskMediaType.Hdd
                    ? "Installe Windows et tes jeux sur un SSD : c'est le changement le plus visible pour la fluidité."
                    : "Si le jeu est installé sur un disque dur (HDD), déplace-le sur ton SSD.");
                break;
        }
        return advice;
    }

    /// <summary>Intel Core de 8e ou 9e génération (socket 1151, cartes mères Z370/Z390/B360…).</summary>
    [GeneratedRegex(@"i[3579]-[89]\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex IntelCoffeeLake();
}
