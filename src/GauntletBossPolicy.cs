using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarGauntlet
{
    internal static class GauntletBossPolicy
    {
        // State families audited in the installed game. Each one needs its own
        // mission scene, another boss's deployable slot or an ambush disguise.
        internal static readonly string[] ScriptedStates =
        {
            "EntityStates.ChampRushReaper.",             // Champion Rush ritual props and phase transition
            "EntityStates.FalseSonBoss.",                // Meridian music, arena center and colossus singleton
            "EntityStates.Providence.",                  // Providence encounter phases; final pulse curses players permanently
            "EntityStates.ProvidenceShadowClone.",
            "EntityStates.Wake.",                        // WakeController sacrifice event
            "EntityStates.SolusWing",                    // Solutional Haunt boss, pods and post-fight
            "EntityStates.SolusHeart.",                  // Conduit Canyon heart phases
            "EntityStates.VoidRaidCrab.EscapeDeath",     // Voidling phases 1-2 open the next Planetarium gauntlet
            "EntityStates.BrotherMonster.SpellChannel",  // Phase-four item theft and moon ending
            "EntityStates.BrotherMonster.TrueDeathState",
            "EntityStates.BrotherMonster.Throne.",
            "EntityStates.BrotherMonster.BrotherPhantom.",
            "EntityStates.BrotherMonster.KarmaSpire.",
            "EntityStates.BrotherHaunt.",
            "EntityStates.TitanMonster.DeathStateGold",  // Gilded Coast Aurelionite
            "EntityStates.Mimic.",                       // Disguised as a chest until damaged
            "EntityStates.Thruster.",                    // Solus Amalgamator detachable parts
            "EntityStates.SolusMine."                    // Worker-unit mine deployable
        };
        // Identity fragments for mission cards whose states look ordinary.
        internal static readonly string[] EncounterNames =
            { "ReaperBoss", "ChampRushReaper", "GildedWurm", "ShrineDeath", "QuestUnlock", "TitanGold", "FalseSonBoss", "SolusWing", "SolusHeart" };
        internal static bool Standalone(string cardName,string bodyName,IEnumerable<string> stateTypes)=>Reason(cardName,bodyName,stateTypes)==null;
        internal static string Reason(string cardName,string bodyName,IEnumerable<string> stateTypes)
        {
            foreach(string name in new[]{cardName,bodyName})
                if(!string.IsNullOrEmpty(name))
                    foreach(string fragment in EncounterNames)
                        if(name.IndexOf(fragment,StringComparison.OrdinalIgnoreCase)>=0)return "encounter-only "+fragment;
            string state=(stateTypes??Array.Empty<string>()).FirstOrDefault(type=>!string.IsNullOrEmpty(type) &&
                ScriptedStates.Any(prefix=>type.StartsWith(prefix,StringComparison.Ordinal)));
            return state==null?null:"scripted state "+state.Split(',')[0];
        }
        // Native bodies the installed language never names are unreleased content.
        internal static bool Released(string token,string display)=>!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(display) && display!=token;
    }
}
