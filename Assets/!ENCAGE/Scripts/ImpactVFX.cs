using FishNet.Object;
using System.Collections;
using UnityEngine;

public class ImpactVFX : NetworkBehaviour
{
    [SerializeField] private AudioClip[] randomSounds;
    [SerializeField] private float lifetime = 0.5f;
    private AudioSource audioSource;

    public override void OnStartClient()
    {
        base.OnStartClient();

        audioSource = GetComponent<AudioSource>();

        if (randomSounds.Length > 0)
        {
            var randomClip = randomSounds[Random.Range(0, randomSounds.Length)];
            audioSource.PlayOneShot(randomClip);
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        StartCoroutine(DespawnAfterTime());
    }

    private IEnumerator DespawnAfterTime()
    {
        yield return new WaitForSeconds(lifetime);
        base.Despawn(DespawnType.Pool);
    }
}