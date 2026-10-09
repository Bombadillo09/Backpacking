using System.Collections.Generic;
using Backpacking.UI;
using Backpacking.Wildlife;
using Unity.Netcode;
using UnityEngine;

namespace Backpacking.Net
{
    /// <summary>Some rabbits and deer as the host's game has them: which, what kind and size, and how each is now.</summary>
    public struct AnimalBatch : INetworkSerializable
    {
        public int[] ids;
        public byte[] kinds;
        public float[] sizes;
        public AnimalSnapshot[] snapshots;

        public int Count => ids?.Length ?? 0;

        public static AnimalBatch Of(List<Animal> animals)
        {
            var batch = new AnimalBatch
            {
                ids = new int[animals.Count],
                kinds = new byte[animals.Count],
                sizes = new float[animals.Count],
                snapshots = new AnimalSnapshot[animals.Count],
            };
            for (int i = 0; i < animals.Count; i++)
            {
                batch.ids[i] = animals[i].NetId;
                batch.kinds[i] = (byte)animals[i].Kind;
                batch.sizes[i] = animals[i].Size;
                batch.snapshots[i] = animals[i].Snapshot();
            }
            return batch;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            int count = Count;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader)
            {
                ids = new int[count];
                kinds = new byte[count];
                sizes = new float[count];
                snapshots = new AnimalSnapshot[count];
            }
            for (int i = 0; i < count; i++)
            {
                serializer.SerializeValue(ref ids[i]);
                serializer.SerializeValue(ref kinds[i]);
                serializer.SerializeValue(ref sizes[i]);
                AnimalSnapshot s = snapshots[i];
                serializer.SerializeValue(ref s.position);
                serializer.SerializeValue(ref s.heading);
                serializer.SerializeValue(ref s.speed);
                serializer.SerializeValue(ref s.state);
                serializer.SerializeValue(ref s.wound);
                serializer.SerializeValue(ref s.watching);
                serializer.SerializeValue(ref s.fieldDressed);
                serializer.SerializeValue(ref s.hideTaken);
                serializer.SerializeValue(ref s.meatLeft);
                serializer.SerializeValue(ref s.arrowsIn);
                serializer.SerializeValue(ref s.deadAtHour);
                snapshots[i] = s;
            }
        }
    }

    /// <summary>
    /// Shared wildlife: the host's game runs the rabbits and deer near any hiker (they take fright at everyone, see
    /// <see cref="World.OtherHikers"/>), and the guests' games show copies that go where the host's go. A guest's arrow
    /// hitting a copy is judged in the host's game, which says how the shot went; taking a rabbit, field dressing a
    /// deer, and taking meat or the hide are passed to the host too, so a carcass is only butchered once. Squirrels,
    /// birds and butterflies stay each game's own.
    /// </summary>
    public partial class CoopWorld
    {
        const float AnimalsEvery = 0.1f;
        const int AnimalsPerMessage = 24;

        readonly Dictionary<int, Animal> animals = new();
        // The host's last sent state of each animal, to send only what moved or changed.
        readonly Dictionary<int, AnimalSnapshot> animalsSent = new();
        readonly Dictionary<int, float> animalsSentAt = new();
        readonly List<Animal> animalBuffer = new();
        int nextAnimal;
        float nextAnimals;

        void SpawnWildlife()
        {
            if (IsServer)
            {
                WildlifeSpawner.AnimalsFromHost = false;
                foreach (Animal animal in Animal.All)
                    if (!animal.Puppet)
                        AddAnimal(animal, send: false);
                Animal.Spawned += OnAnimalSpawned;
                Animal.Removed += OnAnimalRemoved;
            }
            else
            {
                // The host's rabbits and deer replace this game's own (the spawner clears them out).
                WildlifeSpawner.AnimalsFromHost = true;
                Animal.RemoteHit = OnRemoteHit;
                Animal.RemoteAction = OnRemoteAction;
                Animal.Startled = OnStartled;
            }
        }

        void DespawnWildlife()
        {
            Animal.Spawned -= OnAnimalSpawned;
            Animal.Removed -= OnAnimalRemoved;
            if (!IsServer)
            {
                WildlifeSpawner.AnimalsFromHost = false;
                Animal.RemoteHit = null;
                Animal.RemoteAction = null;
                Animal.Startled = null;
                foreach (Animal animal in animals.Values)
                    if (animal != null)
                        Destroy(animal.gameObject);
            }
            animals.Clear();
        }

        // ---------- The host: the animals as they are ----------

        void AddAnimal(Animal animal, bool send)
        {
            animal.NetId = ++nextAnimal;
            animals[animal.NetId] = animal;
            if (send)
                AnimalSpawnRpc(AnimalBatch.Of(new List<Animal> { animal }));
        }

        void OnAnimalSpawned(Animal animal) => AddAnimal(animal, send: true);

        void OnAnimalRemoved(Animal animal)
        {
            if (!animals.Remove(animal.NetId))
                return;
            animalsSent.Remove(animal.NetId);
            animalsSentAt.Remove(animal.NetId);
            if (IsSpawned)
                AnimalGoneRpc(animal.NetId);
        }

        void UpdateWildlife()
        {
            if (!IsServer || Time.unscaledTime < nextAnimals || NetworkManager.ConnectedClientsIds.Count < 2)
                return;
            nextAnimals = Time.unscaledTime + AnimalsEvery;
            animalBuffer.Clear();
            foreach (Animal animal in animals.Values)
            {
                if (animal == null)
                    continue;
                AnimalSnapshot now = animal.Snapshot();
                // Moving or changed: now; standing still: once a second, in case an update was lost.
                if (animalsSent.TryGetValue(animal.NetId, out AnimalSnapshot sent) && Time.unscaledTime - animalsSentAt[animal.NetId] < 1f
                    && (now.position - sent.position).sqrMagnitude < 0.0025f && Mathf.Abs(Mathf.DeltaAngle(now.heading, sent.heading)) < 2f
                    && now.state == sent.state && now.wound == sent.wound && now.watching == sent.watching && now.fieldDressed == sent.fieldDressed
                    && now.hideTaken == sent.hideTaken && now.meatLeft == sent.meatLeft && now.arrowsIn == sent.arrowsIn
                    && Mathf.Abs(now.speed - sent.speed) < 0.2f)
                    continue;
                animalsSent[animal.NetId] = now;
                animalsSentAt[animal.NetId] = Time.unscaledTime;
                animalBuffer.Add(animal);
                if (animalBuffer.Count >= AnimalsPerMessage)
                {
                    AnimalStateRpc(AnimalBatch.Of(animalBuffer));
                    animalBuffer.Clear();
                }
            }
            if (animalBuffer.Count > 0)
                AnimalStateRpc(AnimalBatch.Of(animalBuffer));
        }

        /// <summary>Every animal, for someone joining.</summary>
        void SendAllAnimals(ulong guest)
        {
            animalBuffer.Clear();
            foreach (Animal animal in animals.Values)
            {
                if (animal == null)
                    continue;
                animalBuffer.Add(animal);
                if (animalBuffer.Count >= AnimalsPerMessage)
                {
                    AnimalSpawnToRpc(AnimalBatch.Of(animalBuffer), RpcTarget.Single(guest, RpcTargetUse.Temp));
                    animalBuffer.Clear();
                }
            }
            if (animalBuffer.Count > 0)
                AnimalSpawnToRpc(AnimalBatch.Of(animalBuffer), RpcTarget.Single(guest, RpcTargetUse.Temp));
            animalBuffer.Clear();
        }

        [Rpc(SendTo.Server)]
        void ArrowHitRpc(int id, Vector3 localPoint, Vector3 localDirection, RpcParams rpcParams = default)
        {
            if (!animals.TryGetValue(id, out Animal animal) || animal == null)
                return;
            string said = animal.TakeArrowFromFriend(localPoint, localDirection);
            if (!string.IsNullOrEmpty(said))
                ShotReportRpc(said, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Server)]
        void CarcassRpc(int id, byte action, int amount)
        {
            if (animals.TryGetValue(id, out Animal animal) && animal != null)
                animal.ApplyFriendAction((CarcassAction)action, amount);
        }

        [Rpc(SendTo.Server)]
        void StartleRpc(Vector3 point, float radius) => Animal.StartleNear(point, radius);

        // ---------- A guest: copies of the host's animals ----------

        void OnRemoteHit(Animal animal, Vector3 localPoint, Vector3 localDirection) => ArrowHitRpc(animal.NetId, localPoint, localDirection);

        void OnRemoteAction(Animal animal, CarcassAction action, int amount) => CarcassRpc(animal.NetId, (byte)action, amount);

        void OnStartled(Vector3 point, float radius) => StartleRpc(point, radius);

        [Rpc(SendTo.NotServer)]
        void AnimalSpawnRpc(AnimalBatch batch) => SpawnCopies(batch);

        [Rpc(SendTo.SpecifiedInParams)]
        void AnimalSpawnToRpc(AnimalBatch batch, RpcParams rpcParams) => SpawnCopies(batch);

        void SpawnCopies(AnimalBatch batch)
        {
            WildlifeSpawner spawner = WildlifeSpawner.Current;
            if (spawner == null)
                return;
            for (int i = 0; i < batch.Count; i++)
            {
                if (animals.TryGetValue(batch.ids[i], out Animal known) && known != null)
                {
                    known.ApplySnapshot(batch.snapshots[i]);
                    continue;
                }
                Animal copy = spawner.SpawnCopy((AnimalKind)batch.kinds[i], batch.sizes[i], batch.snapshots[i]);
                copy.NetId = batch.ids[i];
                animals[copy.NetId] = copy;
            }
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void AnimalStateRpc(AnimalBatch batch)
        {
            for (int i = 0; i < batch.Count; i++)
                if (animals.TryGetValue(batch.ids[i], out Animal animal) && animal != null)
                    animal.ApplySnapshot(batch.snapshots[i]);
        }

        [Rpc(SendTo.NotServer)]
        void AnimalGoneRpc(int id)
        {
            if (animals.TryGetValue(id, out Animal animal))
            {
                animals.Remove(id);
                if (animal != null)
                    Destroy(animal.gameObject);
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ShotReportRpc(string said, RpcParams rpcParams) => Notifications.Post(said, 4f);
    }
}
