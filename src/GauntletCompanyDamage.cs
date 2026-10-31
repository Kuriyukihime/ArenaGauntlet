using R2API;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal static class GauntletCompanyDamage
    {
        private static ItemDef marker;
        internal static void Register()
        {
            if(marker)return;
            marker=ScriptableObject.CreateInstance<ItemDef>();
            marker.name="BazaarGauntlet_CompanyDamageReduction";
            marker.nameToken="GAUNTLET_COMPANY_DAMAGE_NAME";
            marker.pickupToken="GAUNTLET_COMPANY_DAMAGE_DESC";
            marker.descriptionToken="GAUNTLET_COMPANY_DAMAGE_DESC";
            marker.loreToken="GAUNTLET_COMPANY_DAMAGE_DESC";
            marker.hidden=true;marker.canRemove=false;
            // Registration precedes ItemTierCatalog initialization. The native
            // fallback supports NoTier without querying an uninitialized catalog.
            NativeAccess.Set(marker,"deprecatedTier",ItemTier.NoTier);
            marker.tags=new[]{ItemTag.AIBlacklist,ItemTag.CannotCopy,ItemTag.CannotSteal,ItemTag.CannotDuplicate,ItemTag.IgnoreForDropList,ItemTag.CannotDisable};
            LanguageAPI.Add(marker.nameToken,"Company damage adjustment");
            LanguageAPI.Add(marker.descriptionToken,"Damage tuning for selected hired units.");
            ContentAddition.AddItemDef(marker);
            RecalculateStatsAPI.GetStatCoefficients+=Recalculate;
            On.RoR2.CharacterMaster.AddDeployable+=Deploy;
        }
        internal static void Apply(CharacterMaster master,int commander,bool leader)
        {
            if(!NetworkServer.active || !master)return;
            int profile=GauntletCompanyDamagePolicy.ProfileFor(commander,leader);
            if(profile==0)return;
            Stamp(master,profile,NaturalSlots());
            // Friendly Titans can remain above 95% health for an entire fight.
            // Their signature laser should be usable without taking damage first.
            // Only this spawned commander changes; native/enemy prefabs stay intact.
            if(commander==8 && leader)
                foreach(var driver in master.GetComponents<AISkillDriver>())
                    if(driver && driver.skillSlot==SkillSlot.Special && driver.maxUserHealthFraction<1)
                        driver.maxUserHealthFraction=float.PositiveInfinity;
        }
        private static int NaturalSlots()=>GauntletNetworkPolicy.CompanyCapacity(Run.instance?Run.instance.participatingPlayerCount:1,3);
        private static void Stamp(CharacterMaster master,int profile,int slots)
        {
            if(!NetworkServer.active || !marker || !master || !master.inventory)return;
            int target=GauntletCompanyDamagePolicy.Encode(profile,slots),current=master.inventory.GetItemCountPermanent(marker);
            // Native inventory replication updates clients/late joiners and
            // invalidates their stats when party size changes. No custom RPC.
            if(target>current)master.inventory.GiveItemPermanent(marker,target-current);
            else if(target<current)master.inventory.RemoveItemPermanent(marker,current-target);
        }
        internal static void RefreshAll(int slots)
        {
            if(!NetworkServer.active || !marker)return;
            foreach(var master in CharacterMaster.readOnlyInstancesList)
                if(master && master.inventory)
                {
                    int profile=GauntletCompanyDamagePolicy.Profile(master.inventory.GetItemCountPermanent(marker));
                    if(profile>0)Stamp(master,profile,slots);
                }
        }
        private static void Deploy(On.RoR2.CharacterMaster.orig_AddDeployable original,CharacterMaster owner,Deployable deployable,DeployableSlot slot)
        {
            original(owner,deployable,slot);
            if(!NetworkServer.active || slot!=DeployableSlot.RoboBallMini || !owner || !deployable)return;
            var company=owner.GetComponent<GauntletCompanyUnit>();
            if(company && company.Commander==6)
                Stamp(deployable.GetComponent<CharacterMaster>(),GauntletCompanyDamagePolicy.SolusProbe,NaturalSlots());
        }
        private static void Recalculate(CharacterBody body,RecalculateStatsAPI.StatHookEventArgs stats)
        {
            if(!marker || !body || !body.inventory || !body.teamComponent || body.teamComponent.teamIndex!=TeamIndex.Player || !GauntletMode.IsActive(Run.instance))return;
            stats.damageTotalMult*=GauntletCompanyDamagePolicy.Multiplier(body.inventory.GetItemCountPermanent(marker));
        }
    }
}
