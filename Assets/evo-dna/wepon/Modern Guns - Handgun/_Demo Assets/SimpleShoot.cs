using UnityEngine;

[AddComponentMenu("Nokobot/Modern Guns/Simple Shoot")]
public class SimpleShoot : MonoBehaviour
{
    private static readonly int FireHash = Animator.StringToHash("Fire");
    private const int PoolSize = 8;

    [Header("Prefab References")]
    public GameObject bulletPrefab;
    public GameObject casingPrefab;
    public GameObject muzzleFlashPrefab;

    [Header("Location References")]
    [SerializeField] private Animator gunAnimator;
    [SerializeField] private Transform barrelLocation;
    [SerializeField] private Transform casingExitLocation;

    [Header("Settings")]
    [SerializeField] private float destroyTimer = 2f;
    [SerializeField] private float shotPower = 500f;
    [SerializeField] private float ejectPower = 150f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip shootSound;

    // Zero-GC Object Pools
    private readonly GameObject[] bulletPool = new GameObject[PoolSize];
    private readonly Rigidbody[] bulletRbs = new Rigidbody[PoolSize];
    private readonly float[] bulletSpawnTimes = new float[PoolSize];
    private int bulletPoolIndex;

    private readonly GameObject[] casingPool = new GameObject[PoolSize];
    private readonly Rigidbody[] casingRbs = new Rigidbody[PoolSize];
    private readonly float[] casingSpawnTimes = new float[PoolSize];
    private int casingPoolIndex;

    private GameObject pooledMuzzleFlash;
    private float muzzleFlashOffTime;

    private void Awake()
    {
        if (barrelLocation == null)
        {
            barrelLocation = transform;
        }
        if (gunAnimator == null)
        {
            gunAnimator = GetComponentInChildren<Animator>();
        }
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        InitializePools();
    }

    private void InitializePools()
    {
        if (muzzleFlashPrefab != null && barrelLocation != null)
        {
            pooledMuzzleFlash = Instantiate(muzzleFlashPrefab, barrelLocation.position, barrelLocation.rotation, barrelLocation);
            pooledMuzzleFlash.SetActive(false);
        }

        if (bulletPrefab != null)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                GameObject b = Instantiate(bulletPrefab);
                b.name = $"Pooled_Bullet_{i}";
                b.SetActive(false);
                bulletPool[i] = b;
                bulletRbs[i] = b.GetComponent<Rigidbody>();
            }
        }

        if (casingPrefab != null)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                GameObject c = Instantiate(casingPrefab);
                c.name = $"Pooled_Casing_{i}";
                c.SetActive(false);
                casingPool[i] = c;
                casingRbs[i] = c.GetComponent<Rigidbody>();
            }
        }
    }

    private void Update()
    {
        float now = Time.time;

        if (pooledMuzzleFlash != null && pooledMuzzleFlash.activeSelf && now >= muzzleFlashOffTime)
        {
            pooledMuzzleFlash.SetActive(false);
        }

        for (int i = 0; i < PoolSize; i++)
        {
            if (bulletPool[i] != null && bulletPool[i].activeSelf && now - bulletSpawnTimes[i] >= 2.5f)
            {
                bulletPool[i].SetActive(false);
            }

            if (casingPool[i] != null && casingPool[i].activeSelf && now - casingSpawnTimes[i] >= destroyTimer)
            {
                casingPool[i].SetActive(false);
            }
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            if (bulletPool[i] != null) Destroy(bulletPool[i]);
            if (casingPool[i] != null) Destroy(casingPool[i]);
        }
    }

    /// <summary>
    /// Được gọi trực tiếp từ HandgunWeapon khi người chơi bấm Chuột trái bắn súng (sử dụng Object Pool 0-GC).
    /// </summary>
    public void TriggerFireEffect(float worldScale)
    {
        if (gunAnimator != null && gunAnimator.runtimeAnimatorController != null)
        {
            gunAnimator.SetTrigger(FireHash);
        }

        if (audioSource != null && shootSound != null)
        {
            audioSource.PlayOneShot(shootSound);
        }

        if (pooledMuzzleFlash != null && barrelLocation != null)
        {
            pooledMuzzleFlash.transform.SetPositionAndRotation(barrelLocation.position, barrelLocation.rotation);
            pooledMuzzleFlash.SetActive(false);
            pooledMuzzleFlash.SetActive(true);
            muzzleFlashOffTime = Time.time + 0.12f;
        }

        if (barrelLocation != null && bulletPool[bulletPoolIndex] != null)
        {
            GameObject tempBullet = bulletPool[bulletPoolIndex];
            Rigidbody rb = bulletRbs[bulletPoolIndex];
            bulletSpawnTimes[bulletPoolIndex] = Time.time;
            bulletPoolIndex = (bulletPoolIndex + 1) % PoolSize;

            tempBullet.SetActive(false);
            tempBullet.transform.SetPositionAndRotation(barrelLocation.position, barrelLocation.rotation);
            tempBullet.transform.localScale = Vector3.one * Mathf.Max(1f, worldScale * 0.5f);
            tempBullet.SetActive(true);

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.AddForce(barrelLocation.forward * (shotPower * Mathf.Max(1f, worldScale * 0.25f)), ForceMode.Impulse);
            }
        }

        CasingRelease(worldScale);
    }

    private void CasingRelease(float worldScale)
    {
        if (casingExitLocation == null || casingPool[casingPoolIndex] == null)
        {
            return;
        }

        GameObject tempCasing = casingPool[casingPoolIndex];
        Rigidbody rb = casingRbs[casingPoolIndex];
        casingSpawnTimes[casingPoolIndex] = Time.time;
        casingPoolIndex = (casingPoolIndex + 1) % PoolSize;

        tempCasing.SetActive(false);
        tempCasing.transform.SetPositionAndRotation(casingExitLocation.position, casingExitLocation.rotation);
        tempCasing.transform.localScale = Vector3.one * Mathf.Max(1f, worldScale * 0.6f);
        tempCasing.SetActive(true);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            float scaledEject = ejectPower * Mathf.Max(1f, worldScale * 0.15f);
            Vector3 explosionPos = casingExitLocation.position - casingExitLocation.right * 0.3f - casingExitLocation.up * 0.6f;
            rb.AddExplosionForce(Random.Range(scaledEject * 0.7f, scaledEject), explosionPos, 1f);
            rb.AddTorque(new Vector3(0f, Random.Range(100f, 500f), Random.Range(100f, 1000f)), ForceMode.Impulse);
        }
    }
}
