using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Artifacts;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace BazaarGauntlet
{
    internal sealed class GauntletPortalLoot : MonoBehaviour
    {
        private readonly List<GenericPickupController.CreatePickupInfo> carried = new List<GenericPickupController.CreatePickupInfo>();
        // Owner of each carried reward, in step with carried.
        private readonly List<NetworkInstanceId> carriedOwners = new List<NetworkInstanceId>();
        private int nextPlayer;
        private static GameObject optionPrefab;
        private bool clearWarning;
        private static bool PaidStock(Component item) => item.GetComponent<GauntletPaidLoot>() || item.GetComponentInParent<FortressServiceTag>() || item.GetComponentInParent<PurchaseInteraction>() || item.GetComponentInParent<ShopTerminalBehavior>();
        internal void Collect()=>Collect(false);
        private void Collect(bool leaveChoicesInPlace)
        {
            var arena=GetComponent<GauntletController>();
            if (!NetworkServer.active || !arena || !arena.Fortress.InCastle || !GauntletLootPolicy.Sweep(GauntletRounds.IsBazaar)) return;
            // Bazaar displays and modded shop choice objects are not free loot.
            // No parent/component heuristic can prove that they were purchased.
            // Settle airborne rewards through the native path, preserving
            // Command, UniquePickup flags and choice pickup creation.
            foreach (var droplet in InstanceTracker.GetInstancesList<PickupDropletController>().ToArray())
                if (droplet && !PaidStock(droplet)) droplet.ForcePickupInitialization();
            var players = PlayerCharacterMasterController.instances.Where(player => player && player.isConnected && player.master)
                .Select(player => player.master.GetBody()).Where(body => body && body.healthComponent && body.healthComponent.alive && body.inventory).ToArray();
            foreach (var pickup in InstanceTracker.GetInstancesList<GenericPickupController>().ToArray())
            {
                if (!pickup || PaidStock(pickup) || pickup.GetComponent<PickupPickerController>() || NativeAccess.Get<bool>(pickup, "consumed")) continue;
                var def = PickupCatalog.GetPickupDef(pickup.pickup.pickupIndex);
                if (def == null) continue;
                float wait = pickup.waitDuration;
                bool granted = false;
                try
                {
                    // Departure replaces the cosmetic drop-settle delay only.
                    // Native pickup permission and item-grant hooks still run.
                    pickup.waitDuration = 0;
                    for (int offset = 0; offset < players.Length; offset++)
                    {
                        int index = (nextPlayer + offset) % players.Length;
                        var body = players[index];
                        if (def.equipmentIndex != EquipmentIndex.None && body.inventory.currentEquipmentIndex != EquipmentIndex.None) continue;
                        var interactor = body.GetComponent<Interactor>();
                        if (!interactor || pickup.GetInteractability(interactor) != Interactability.Available) continue;
                        pickup.OnInteractionBegin(interactor);
                        if (!pickup || NativeAccess.Get<bool>(pickup, "consumed"))
                        { nextPlayer = (index + 1) % players.Length;granted = true;break; }
                    }
                }
                finally { if (pickup) pickup.waitDuration = wait; }
                if (granted || leaveChoicesInPlace) continue;
                carried.Add(new GenericPickupController.CreatePickupInfo
                { pickup = pickup.pickup, rotation = pickup.transform.rotation, duplicated = pickup.Duplicated, recycled = pickup.Recycled });
                carriedOwners.Add(arena.Loot.OwnerOf(pickup));
                NetworkServer.Destroy(pickup.gameObject);
            }
            if(leaveChoicesInPlace)return;
            foreach (var picker in InstanceTracker.GetInstancesList<PickupPickerController>().ToArray())
            {
                if (!picker || PaidStock(picker) || !picker.available || !picker.pickupThatGeneratedOptions.HasValue) continue;
                int remaining = NativeAccess.Get<int>(picker, "_grabNumbers") - NativeAccess.Get<int>(picker, "numberOfPickUpsSelected");
                if (remaining <= 0) continue;
                bool command = picker.name.IndexOf("CommandCube", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!command && !optionPrefab)
                    optionPrefab = Addressables.LoadAssetAsync<GameObject>(RoR2BepInExPack.GameAssetPathsBetter.RoR2_DLC1_OptionPickup.OptionPickup_prefab).WaitForCompletion();
                var prefab = command ? CommandArtifactManager.commandCubePrefab : optionPrefab;
                if (!prefab) { GauntletPlugin.Log.LogWarning("Unable to preserve a choice pickup: its native prefab is unavailable.");continue; }
                carried.Add(new GenericPickupController.CreatePickupInfo
                {
                    pickup = picker.pickupThatGeneratedOptions.Value, pickerOptions = (PickupPickerController.Option[])picker.options.Clone(),
                    maxOptionCount = remaining, prefabOverride = prefab, rotation = Quaternion.identity
                });
                carriedOwners.Add(arena.Loot.OwnerOf(picker));
                NetworkServer.Destroy(picker.gameObject);
            }
        }
        internal void RoundCleared()
        {
            if(!NetworkServer.active)return;
            clearWarning=false;CollectInPlace();StartCoroutine(CollectAfterClear());
        }
        private void CollectInPlace()
        {
            try {Collect(true);}
            catch(Exception error)
            {if(!clearWarning)GauntletPlugin.Log.LogWarning("A free-drop grant failed; round progression continues and collection will retry. "+error);clearWarning=true;}
        }
        private IEnumerator CollectAfterClear()
        {
            var owner=GetComponent<GauntletController>();int round=owner.Flow.Round,cycle=owner.Flow.Cycle;
            // Boss rewards and delayed death drops can appear after the clear event.
            // Retry through defender respawns without collecting paid stock or
            // moving open choice rewards out from under another player's cursor.
            for(int attempt=0;attempt<24;attempt++)
            {
                yield return new WaitForSeconds(attempt==0?.6f:1);
                if(!owner || owner.ShuttingDown || !owner.CollectLoot || owner.Flow.Round!=round || owner.Flow.Cycle!=cycle || !owner.Fortress.CanManage)yield break;
                CollectInPlace();
            }
        }
        internal void Arrived() { if (NetworkServer.active && GauntletLootPolicy.Restore(GauntletRounds.IsBazaar) && carried.Count > 0) StartCoroutine(Restore()); }
        private IEnumerator Restore()
        {
            yield return new WaitForSeconds(1);
            var owner = GetComponent<GauntletController>();
            if (!owner || !SceneInfo.instance || !owner.Fortress.InCastle || GauntletRounds.IsBazaar) yield break;
            CharacterBody body = null;
            for (int attempt = 0; attempt < 40 && !body; attempt++)
            {
                body = PlayerCharacterMasterController.instances.Where(player => player && player.isConnected && player.master)
                    .Select(player => player.master.GetBody()).FirstOrDefault(value => value);
                if (!body) yield return new WaitForSeconds(.25f);
            }
            var ward = NativeAccess.Get<InfiniteTowerSafeWardController>(owner.Run, "safeWardController");
            if (!body && !ward)
            {
                GauntletPlugin.Log.LogWarning("Choice rewards retained for the next arrival: no valid player/ward spawn position yet.");
                yield break;
            }
            Vector3 center = body ? body.footPosition : ward.transform.position;
            var rewards = carried.ToArray();var players = carriedOwners.ToArray();carried.Clear();carriedOwners.Clear();
            for (int i = 0; i < rewards.Length; i++)
            {
                var info = rewards[i];float angle = i * 2.399963f;
                info.position = center + new Vector3(Mathf.Cos(angle), .5f, Mathf.Sin(angle)) * (2 + .12f * i);
                owner.Loot.Claim(players[i], () => GenericPickupController.CreatePickup(in info));
            }
        }
    }
}
