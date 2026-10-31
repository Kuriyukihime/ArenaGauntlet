namespace BazaarGauntlet
{
    internal static class GauntletCurrencyPolicy
    {
        // Only accepted arena/Bazaar departures. Other modes, exits and native
        // teleport/finished states retain their original behavior.
        internal static bool BlockTransferDebit(bool mode,bool server,bool transferring,bool player,uint value)=>mode && server && transferring && player && value==0;
        internal static bool BlockExtraction(bool mode,bool server,bool accepted,bool extracting)=>mode && server && accepted && extracting;
    }
}
