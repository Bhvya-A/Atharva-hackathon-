namespace TheOtherSide
{
    /// <summary>
    /// The four collectible relics. Their gameplay role is intentionally identical
    /// (carry one, deposit it at the ritual table); only the visual model, world
    /// placement and, for the Grimoire, the locked-container gate differ.
    /// </summary>
    public enum RelicType
    {
        Skull = 0,
        Grimoire = 1,
        Candle = 2,
        Compass = 3
    }

    public static class RelicTypes
    {
        public static string DisplayName(RelicType type)
        {
            switch (type)
            {
                case RelicType.Skull: return "SKULL";
                case RelicType.Grimoire: return "GRIMOIRE";
                case RelicType.Candle: return "CANDLE";
                case RelicType.Compass: return "COMPASS";
                default: return "RELIC";
            }
        }

        /// <summary>Resources path of the normalized, hold-ready relic prefab.</summary>
        public static string ResourcePath(RelicType type)
        {
            return "Relics/" + type;
        }
    }
}
