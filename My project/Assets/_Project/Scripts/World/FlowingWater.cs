using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// Moves a water surface's ripples along: down a river (its mesh's V runs downstream, a repeat every 6 m), or
    /// drifting slowly across a lake. Per renderer, so the shared material asset isn't touched.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class FlowingWater : MonoBehaviour
    {
        [Tooltip("Metres per second the water runs.")]
        [SerializeField] float speed = 0.6f;
        [Tooltip("Ripple repeats across and along the surface's UVs.")]
        [SerializeField] Vector2 tiling = Vector2.one;
        [Tooltip("Metres the mesh's V coordinate covers per repeat (a river strip's is 6).")]
        [SerializeField] float metresPerRepeat = 6f;

        static readonly int TransformId = Shader.PropertyToID("_BaseMap_ST");

        Renderer surface;
        MaterialPropertyBlock block;

        void Awake()
        {
            surface = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (!surface.isVisible)
                return;
            float offset = -Time.time * speed / metresPerRepeat;
            surface.GetPropertyBlock(block);
            block.SetVector(TransformId, new Vector4(tiling.x, tiling.y, offset * 0.15f, offset));
            surface.SetPropertyBlock(block);
        }
    }
}
