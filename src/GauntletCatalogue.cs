using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoR2;
using RoR2.ContentManagement;
using RoR2.ExpansionManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarGauntlet
{
    internal static class GauntletCatalogue
    {
        private sealed class Entry
        {
            internal CharacterSpawnCard Card;
            internal CharacterBody Body;
            internal string Theme,CardName,BodyName,BodyToken;
            internal int Species,Cost;
            internal float Health,Damage;
            internal bool Alive=>Card && Card.prefab && Body;
        }
        private static readonly List<CharacterSpawnCard> nativeCards = new List<CharacterSpawnCard>();
        private static readonly List<CharacterSpawnCard> adapters = new List<CharacterSpawnCard>();
        private static readonly List<Entry> ordinary = new List<Entry>(), bosses = new List<Entry>(), great = new List<Entry>();
        private static readonly List<DirectorCardCategorySelection> decks = new List<DirectorCardCategorySelection>();
        private static bool nativeLoaded;
        internal static bool Available(GameObject prefab, Run run)
        {
            var requirement = prefab ? prefab.GetComponent<ExpansionRequirementComponent>() : null;
            return !requirement || !requirement.requiredExpansion || run.IsExpansionEnabled(requirement.requiredExpansion);
        }
        internal static void Prepare(GauntletController owner)
        {
            foreach (var deck in decks) if (deck) UnityEngine.Object.Destroy(deck);
            decks.Clear(); ordinary.Clear(); bosses.Clear(); great.Clear();
            foreach (var adapter in adapters) if (adapter) UnityEngine.Object.Destroy(adapter);
            adapters.Clear();
            if (!nativeLoaded)
            {
                // The installed game's generated path registry supplies actual
                // addressable keys, including newer DLC. No guessed DLC roster.
                var assembly = typeof(RoR2BepInExPack.GameAssetPathsBetter.RoR2_DLC1_GameModes_InfiniteTowerRun).Assembly;
                var paths = assembly.GetTypes().Where(type => type.Namespace == "RoR2BepInExPack.GameAssetPathsBetter" && !type.Name.Contains("_Junk_"))
                    .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                    .Where(field => field.FieldType == typeof(string) && field.Name.StartsWith("csc", StringComparison.Ordinal) && field.Name.EndsWith("_asset", StringComparison.Ordinal))
                    // These are version-aware static readonly strings, NOT
                    // literal path constants. Let the pack resolve each key.
                    .Select(field => field.GetValue(null) as string).Where(path => !string.IsNullOrEmpty(path)).Distinct();
                foreach (var path in paths)
                {
                    foreach (var locator in Addressables.ResourceLocators)
                    {
                        if (!locator.Locate(path, typeof(CharacterSpawnCard), out var locations) || locations.Count == 0) continue;
                        var card = Addressables.LoadAssetAsync<CharacterSpawnCard>(locations[0]).WaitForCompletion();
                        if (card) nativeCards.Add(card);
                        break;
                    }
                }
                nativeLoaded = true;
            }
            var packs = new Dictionary<GameObject, string>();
            foreach (var pack in ContentManager.allLoadedContentPacks)
                foreach (var body in pack.bodyPrefabs) if (body) packs[body] = pack.identifier;
            // Include registered mod decks as well as native cards; do not
            // manufacture spawns from arbitrary survivor/master prefabs.
            var cards = new HashSet<CharacterSpawnCard>(nativeCards);
            foreach (var deck in Resources.FindObjectsOfTypeAll<DirectorCardCategorySelection>())
            {
                // Destroy is deferred. Old generated decks/cards are still
                // discoverable during Quick Restart, but must never be sources.
                if(!deck || !GauntletCatalogueRules.ExternalSource(deck.name) || deck.categories==null)continue;
                foreach(var category in deck.categories)
                    foreach(var entry in category.cards??Array.Empty<DirectorCard>())
                        if(entry!=null && entry.GetSpawnCard() is CharacterSpawnCard card && card && GauntletCatalogueRules.ExternalSource(card.name))cards.Add(card);
            }
            var excluded=new SortedSet<string>(StringComparer.Ordinal);
            foreach (var card in cards)
            {
                if (!card || !GauntletCatalogueRules.ExternalSource(card.name) || !card.prefab || !Available(card.prefab, owner.Run)) continue;
                var master = card.prefab.GetComponent<CharacterMaster>();
                var prefab = master ? master.bodyPrefab : null;
                var body = prefab ? prefab.GetComponent<CharacterBody>() : null;
                if (!body || !Available(prefab, owner.Run)) continue;
                // Spawn, idle and death states all count: several mission bosses
                // look ordinary until the death state reaches for its scene.
                var death=prefab.GetComponent<CharacterDeathBehavior>();
                string reason=GauntletBossPolicy.Reason(card.name,prefab.name,prefab.GetComponents<EntityStateMachine>()
                    .SelectMany(machine=>new[]{machine.initialStateType.typeName,machine.mainStateType.typeName})
                    .Concat(new[]{death?death.deathState.typeName:null}));
                if(reason==null && packs.TryGetValue(prefab,out var source) && source.StartsWith("RoR2.",StringComparison.Ordinal) &&
                    !GauntletBossPolicy.Released(body.baseNameToken,Language.GetString(body.baseNameToken)))reason="unreleased (no display name)";
                if(reason!=null){excluded.Add(card.name+": "+reason);continue;}
                string key = (card.name + " " + prefab.name).ToLowerInvariant();
                if (new[] { "ally", "friendly", "drone", "turret", "dummy", "invades", "infestor", "parasite", "healingcore", "shopkeeper", "throne", "phantom", "glass" }.Any(key.Contains)) continue;
                if (SurvivorCatalog.FindSurvivorDefFromBody(prefab)) continue;
                var requirement = prefab.GetComponent<ExpansionRequirementComponent>();
                string theme = requirement && requirement.requiredExpansion ? Language.GetString(requirement.requiredExpansion.nameToken) :
                    packs.TryGetValue(prefab, out var pack) ? pack : "Base game";
                if (key.Contains("brotherhurt") || key.Contains("voidraidcrab") && !key.Contains("phase3")) continue;
                var entry = NewEntry(card,body,theme);
                if (key.Contains("brother") || key.Contains("scavlunar") || key.Contains("minivoidraidcrabphase3"))
                {
                    var adapted = UnityEngine.Object.Instantiate(card);
                    adapted.name = "BazaarGauntlet_" + card.name; adapted.hullSize = HullClassification.Human;
                    // Native ScavLunar requires TeleporterOK nodes; the generated arena grid
                    // has none, so Twisted Scavengers timed out into the fallback champion.
                    adapted.requiredFlags = RoR2.Navigation.NodeFlags.None;
                    adapters.Add(adapted); entry.Card = adapted; great.Add(entry); continue;
                }
                if (body.isChampion && !card.forbiddenAsBoss) bosses.Add(entry);
                else if (!body.isChampion && card.directorCreditCost > 0 && prefab.GetComponent<DeathRewards>()) ordinary.Add(entry);
            }
            if (ordinary.Count == 0 || bosses.Count == 0) throw new InvalidOperationException("No enabled ordinary enemy/boss spawn cards were found.");
            foreach(string line in excluded)GauntletPlugin.Log.LogInfo("Excluded from arena pools - "+line);
            GauntletPlugin.Log.LogInfo($"Arena pools: {ordinary.Count} enemies, {bosses.Count} ordinary bosses, {great.Count} major-boss arena adapters; {excluded.Count} scripted/unreleased cards excluded.");
        }
        private static Entry NewEntry(CharacterSpawnCard card,CharacterBody body,string theme)=>new Entry
        {Card=card,Body=body,Theme=theme,CardName=card.name,BodyName=body.name,BodyToken=body.baseNameToken,Species=body.gameObject.GetInstanceID(),
         Cost=GauntletBalance.BossCost(card.directorCreditCost,body.baseMaxHealth),Health=body.baseMaxHealth,Damage=body.baseDamage};
        internal static DirectorCardCategorySelection HordeDeck(GauntletController owner)
        {
            int round=owner.Flow.Round,cycle=owner.Flow.Cycle,costLimit = GauntletBalance.HordeCostCeiling(round,cycle);
            var valid=ordinary.Where(entry=>entry.Alive).ToArray();
            if(valid.Length==0)throw new InvalidOperationException("No live horde cards remain; the catalogue must be refreshed.");
            var eligible = valid.Where(entry => entry.Card.directorCreditCost <= costLimit)
                .GroupBy(entry=>entry.Species).Select(group=>group.OrderBy(entry=>entry.Card.directorCreditCost).ThenBy(entry=>entry.CardName,StringComparer.Ordinal).First())
                .OrderBy(entry=>entry.BodyName,StringComparer.Ordinal).ToArray();
            if (eligible.Length == 0) eligible = valid.OrderBy(entry => entry.Card.directorCreditCost).Take(4).ToArray();
            var themes = eligible.Select(entry => entry.Theme).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray();
            string theme = themes[owner.ArenaRng.RangeInt(0, themes.Length)];
            owner.ThemeName = theme+" · mixed assault";
            bool opening = owner.Flow.Cycle == 1 && owner.Flow.Round <= 2;
            var deck = ScriptableObject.CreateInstance<DirectorCardCategorySelection>();deck.name = "BazaarGauntlet_" + theme;
            deck.categories = new[] { new DirectorCardCategorySelection.Category
            {
                name = "Arena horde", selectionWeight = 1,
                // All castle assaults begin outside the permanent footprint.
                // A theme biases the mix; it must never exclude every other
                // species when that content pack only supplies one cheap enemy.
                cards = eligible.Select(entry => HordeCard(entry, opening, GauntletBalance.SpeciesWeight(entry.Card.directorCreditCost,round,cycle,entry.Theme==theme))).ToArray()
            } };
            GauntletPlugin.Log.LogInfo($"Horde roster: {eligible.Length} distinct bodies, theme {theme}; cost ceiling {(costLimit==int.MaxValue?"open":costLimit.ToString())}. "+
                string.Join(", ",eligible.Select(entry=>Language.GetString(entry.BodyToken))));
            decks.Add(deck);return deck;
        }
        private static DirectorCard HordeCard(Entry entry, bool opening, int weight)
        {
            var card = entry.Card;
            if (opening)
            {
                card = UnityEngine.Object.Instantiate(card);
                card.name = "BazaarGauntletOpening_" + entry.CardName;
                card.noElites = true;
                adapters.Add(card);
            }
            return new DirectorCard { spawnCard = card, selectionWeight = weight,
                minimumStageCompletions = 0, preventOverhead = true,
                spawnDistance = DirectorCore.MonsterSpawnDistance.Standard };
        }
        internal static CharacterSpawnCard Boss(GauntletController owner, bool fallback = false, CharacterSpawnCard previous = null)
        {
            int round=owner.Flow.Round,cycle=owner.Flow.Cycle;
            bool major=owner.Flow.BossKind != GauntletBossKind.Ordinary && !fallback && great.Count > 0;
            var live=(major?great:bosses).Where(entry=>entry.Alive).ToArray();
            if(live.Length==0)throw new InvalidOperationException("No live enabled boss cards remain.");
            Entry[] eligible;
            if(major)
            {
                GauntletBalance.GreatTierRange(round,cycle,out int low,out int high);
                int Tier(Entry entry)=>GauntletBalance.GreatTier(entry.CardName+" "+entry.BodyName);
                eligible=live.Where(entry=>Tier(entry)>=low && Tier(entry)<=high).ToArray();
                if(eligible.Length==0)eligible=live.GroupBy(entry=>Math.Abs(Tier(entry)-high)).OrderBy(group=>group.Key).First().ToArray();
            }
            else
            {
                // Round-appropriate champions, by the native director's own cost bands.
                int ceiling=GauntletBalance.BossCostCeiling(round,cycle),floor=GauntletBalance.BossCostFloor(round,cycle);
                bool opening=cycle==1 && GauntletPlan.OriginalRound(round)<=2;
                eligible=live.Where(entry=>entry.Cost<=ceiling && entry.Cost>=floor && (!opening || GauntletBalance.OpeningBoss(entry.Health,entry.Damage))).ToArray();
                if(eligible.Length==0)eligible=live.OrderBy(entry=>Math.Abs((long)entry.Cost-Math.Min(ceiling,4000))).ThenBy(entry=>entry.Health).Take(3).ToArray();
            }
            if(previous && previous.prefab)
            {
                var previousMaster=previous.prefab.GetComponent<CharacterMaster>();
                var previousPrefab=previousMaster?previousMaster.bodyPrefab:null;
                if(previousPrefab)
                {
                    // Species is the body GameObject's ID (NewEntry), not the CharacterBody component's.
                    var different=eligible.Where(candidate=>candidate.Species!=previousPrefab.GetInstanceID()).ToArray();
                    if(different.Length>0)eligible=different;
                }
            }
            // Pick a species first: four Titan or two Beetle Queen card variants
            // must not crowd out other champions.
            var species=eligible.GroupBy(entry=>entry.Species).OrderBy(group=>group.First().BodyName,StringComparer.Ordinal).ToArray();
            var chosen=species[owner.ArenaRng.RangeInt(0,species.Length)].OrderBy(entry=>entry.CardName,StringComparer.Ordinal).ToArray();
            var entry=chosen[owner.ArenaRng.RangeInt(0,chosen.Length)];
            owner.ThemeName = Language.GetString(entry.BodyToken);
            return entry.Card;
        }
    }
}
