using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Static mesh produced from the skinned renderers, plus the source material of every submesh
    /// in submesh order.
    /// </summary>
    internal sealed class VatMeshResult
    {
        public VatMeshResult(Mesh mesh, Material[] sourceMaterials)
        {
            Mesh = mesh;
            SourceMaterials = sourceMaterials;
        }

        public Mesh Mesh { get; }
        public Material[] SourceMaterials { get; }
    }
}
