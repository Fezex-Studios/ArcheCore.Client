using ArchCore.Client;
using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    // Attach to Main Camera in the World scene.
    // Drag nothing — it finds the local player automatically once spawned.
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0f, 8f, -6f);
        [SerializeField] private float smoothSpeed = 8f;

        private Transform target;

        private void Update()
        {
            // Keep trying until the local player is spawned
            if (target == null)
            {
                FindLocalPlayer();
                return;
            }

            // Smooth follow
            Vector3 desired = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * smoothSpeed);

            // Always look at the player
            transform.LookAt(target.position);
        }

        private void FindLocalPlayer()
        {
            // Find all PlayerControllers in the scene and grab the local one
            foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (pc.isLocalPlayer)
                {
                    target = pc.transform;
                    return;
                }
            }
        }
    }
}