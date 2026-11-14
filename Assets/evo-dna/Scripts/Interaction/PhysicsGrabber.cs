using UnityEngine;

namespace EvoDNA.Interaction
{
    // Giữ lại class để không gây Missing Script trên Scene cũ; tự động gỡ bỏ khi khởi chạy
    public class PhysicsGrabber : MonoBehaviour
    {
        private void Awake()
        {
            Destroy(this);
        }
    }
}
