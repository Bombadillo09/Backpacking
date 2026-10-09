using Backpacking.Net;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Backpacking.EditorTools
{
    /// <summary>
    /// The two network prefabs co-op spawns, kept in Resources so <see cref="CoopSession"/> can load them: the
    /// hiker each player gets (NGO's player prefab) and the shared world the host spawns. Made by the scene build
    /// when missing; delete them to make them again.
    /// </summary>
    public static class CoopSetup
    {
        const string Folder = "Assets/_Project/Resources/Net";

        public static string EnsurePrefabs()
        {
            EnsureFolder("Assets/_Project/Resources");
            EnsureFolder(Folder);
            Make("CoopHiker", go =>
            {
                go.AddComponent<NetworkObject>();
                var hiker = go.AddComponent<CoopHiker>();
                var serialized = new SerializedObject(hiker);
                serialized.FindProperty("library").objectReferenceValue = CharacterSetup.GetOrCreateLibrary();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            });
            Make("CoopWorld", go =>
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<CoopWorld>();
            });
            return string.Join(", ", System.Array.ConvertAll(new[] { "CoopHiker", "CoopWorld" },
                name => $"{name} {AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{name}.prefab").GetComponent<NetworkObject>().PrefabIdHash}"));
        }

        static void Make(string name, System.Action<GameObject> build)
        {
            string path = $"{Folder}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing == null)
            {
                var go = new GameObject(name);
                try
                {
                    build(go);
                    PrefabUtility.SaveAsPrefabAsset(go, path);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            // NGO gives a prefab its network id once it's an asset; saving it again from the asset sets it.
            if (existing.GetComponent<NetworkObject>().PrefabIdHash == 0)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }
            uint hash = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>().PrefabIdHash;
            if (hash == 0)
                Debug.LogError($"{path} has no network id; co-op can't spawn it.");
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            int slash = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }
    }
}
