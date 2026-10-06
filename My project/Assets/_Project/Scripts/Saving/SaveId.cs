using UnityEngine;

namespace Backpacking.Saving
{
    /// <summary>
    /// A stable name for an object placed in the scene, so a save file can refer to it
    /// (e.g. "this firewood was collected"). Set by the scene builder; must be unique within the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class SaveId : MonoBehaviour
    {
        [SerializeField] string id;

        public string Id => id;
    }
}
