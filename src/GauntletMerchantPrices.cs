using System;
using RoR2;
using UnityEngine;

namespace BazaarGauntlet
{
    internal static class GauntletMerchantPrices
    {
        internal static int SmallChest(Run run)
        {
            // BiggerBazaar's default uses the arena coefficient after stage
            // zero, and its own raw-minute calculation for the opening stage.
            float coefficient=run.difficultyCoefficient;
            if(run.stageClearCount==0)
            {
                float players=run.participatingPlayerCount;
                float minutes=Mathf.Floor(run.GetRunStopwatch()*(1f/60f));
                coefficient=.7f+players*.3f+.046f*DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty).scalingValue*Mathf.Pow(players,.2f)*minutes;
            }
            return (int)Math.Min(int.MaxValue,Math.Max(1,25d*Mathf.Pow(coefficient,1.25f)));
        }
    }
}
