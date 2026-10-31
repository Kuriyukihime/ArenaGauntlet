namespace BazaarGauntlet
{
    internal enum FortressEnemyRole { Assault, Raider, Saboteur, Breaker, Commander }
    internal static class GauntletEnemyRoles
    {
        // Body/AI traits cover expansion and registered mod enemies without a
        // fragile species whitelist. Heavy ranged units still act as breakers.
        internal static FortressEnemyRole Classify(bool champion,bool flying,float health,float speed,bool ranged)
        {
            if(champion)return FortressEnemyRole.Commander;
            if(health>=750 && !flying)return FortressEnemyRole.Breaker;
            if(ranged)return FortressEnemyRole.Saboteur;
            if(flying)return FortressEnemyRole.Raider;
            if(speed>=8 || health<250)return FortressEnemyRole.Raider;
            return FortressEnemyRole.Assault;
        }
    }
}
