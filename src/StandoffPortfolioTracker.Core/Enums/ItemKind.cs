using System.ComponentModel.DataAnnotations;

namespace StandoffPortfolioTracker.Core.Enums
{
    public enum ItemKind
    {
        [Display(Name = "None")]
        None = 0,

        // --- Container Subtypes ---
        Box,
        Crate,
        Case,

        [Display(Name = "Sticker Pack")]
        StickerPack,

        [Display(Name = "Graffiti Pack")]
        GraffitiPack,

        [Display(Name = "Charm Pack")]
        CharmPack,              
        Other,

        // --- Knife Subtypes (на будущее) ---
        Karambit,
        Butterfly,
        [Display(Name = "M9 Bayonet")]
        M9Bayonet,
        // ... и так далее

        Etc = 999
    }
}