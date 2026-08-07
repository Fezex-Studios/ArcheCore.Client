using UnityEngine;

namespace ArcheCore.Client
{
    public class NetworkCels : MonoBehaviour
    {
        private const float CellSize = 50f;
        private const int RadiusCells = 1;

        private void OnDrawGizmos()
        {
            Vector3 pos = transform.position;

            Gizmos.color = new Color(0f, 1f, 0.4f, 0.15f);
            Vector3 center = new Vector3(
                Mathf.Floor(pos.x / CellSize) * CellSize + CellSize / 2f,
                pos.y,
                Mathf.Floor(pos.z / CellSize) * CellSize + CellSize / 2f);
            float size = CellSize * (RadiusCells * 2 + 1);
            Gizmos.DrawCube(new Vector3(center.x, pos.y, center.z), new Vector3(size, 0.1f, size));

            Gizmos.color = Color.gray;
            for (float x = -500; x <= 500; x += CellSize)
                Gizmos.DrawLine(new Vector3(x, pos.y, -500), new Vector3(x, pos.y, 500));
            for (float z = -500; z <= 500; z += CellSize)
                Gizmos.DrawLine(new Vector3(-500, pos.y, z), new Vector3(500, pos.y, z));
        }
    }
}