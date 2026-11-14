using UnityEngine;

public class gunDestroy : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null && collision.collider != null && collision.collider.CompareTag("Player"))
        {
            return;
        }

        // Sát thương chính xác đã được xử lý bằng Hitscan SphereCast trong HandgunWeapon.TryFire().
        // Thu hồi đầu đạn về Object Pool ngay khi chạm mục tiêu/tường để không phát sinh rác bộ nhớ (Zero-GC).
        gameObject.SetActive(false);
    }
}
