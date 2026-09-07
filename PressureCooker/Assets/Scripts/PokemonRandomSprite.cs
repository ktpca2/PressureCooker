using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// Attach this script to a GameObject that has a SpriteRenderer component
// (an empty "Sprite 2D" GameObject works perfectly).
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(AudioSource))]
public class PokemonRandomSprite : MonoBehaviour
{
    [Header("Pokemon Range")]
    [Tooltip("PokeAPI currently has Pokemon numbered 1 to 1025 (Gen 1-9).")]
    [SerializeField] private int minPokedexId = 1;
    [SerializeField] private int maxPokedexId = 1025;

    [Tooltip("If true, starts the cycle-and-land sequence automatically on Start.")]
    [SerializeField] private bool fetchOnStart = true;

    [Header("Cycling / Slot-Machine Effect")]
    [Tooltip("How many random sprites are pre-loaded and flipped through before landing.")]
    [SerializeField] private int cyclePoolSize = 12;

    [Tooltip("Delay (seconds) between sprite swaps at the START of the cycle (fast).")]
    [SerializeField] private float startInterval = 0.04f;

    [Tooltip("Delay (seconds) between sprite swaps at the END of the cycle (slow, right before landing).")]
    [SerializeField] private float endInterval = 0.35f;

    [Header("Shiny Settings")]
    [Tooltip("Percent chance (0-100) that the landed Pokemon is shiny.")]
    [Range(0f, 100f)]
    [SerializeField] private float shinyChancePercent = 5f;

    [Header("Audio")]
    [Tooltip("Sound played on every sprite swap during the cycle.")]
    [SerializeField] private AudioClip cycleTickSound;

    [Tooltip("Sound played when a normal (non-shiny) Pokemon is revealed. Falls back to the tick sound if empty.")]
    [SerializeField] private AudioClip landSound;

    [Tooltip("Sound played instead of Land Sound when a shiny Pokemon is revealed.")]
    [SerializeField] private AudioClip shinyLandSound;

    private SpriteRenderer spriteRenderer;
    private AudioSource audioSource;
    private bool isRolling;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void Start()
    {
        if (fetchOnStart)
        {
            FetchRandomPokemonSprite();
        }
    }

    /// <summary>
    /// Public entry point — call this from a button, event, or another script
    /// whenever you want to roll a new random Pokemon with the cycling effect.
    /// </summary>
    public void FetchRandomPokemonSprite()
    {
        if (isRolling) return; // prevent overlapping rolls
        StartCoroutine(RollRoutine());
    }

    private IEnumerator RollRoutine()
    {
        isRolling = true;

        // 1) Decide the final Pokemon and whether it's shiny, up front.
        int finalId = Random.Range(minPokedexId, maxPokedexId + 1);
        bool finalIsShiny = Random.Range(0f, 100f) < shinyChancePercent;

        // 2) Build a request list: cycling frames (never shiny) plus the final
        //    entry (which may be shiny) appended last, so the pool guarantees
        //    it ends on the correct sprite.
        List<PokemonRequest> requests = new List<PokemonRequest>();
        for (int i = 0; i < cyclePoolSize; i++)
        {
            requests.Add(new PokemonRequest(Random.Range(minPokedexId, maxPokedexId + 1), false));
        }
        requests.Add(new PokemonRequest(finalId, finalIsShiny));

        List<PokemonResult> pool = new List<PokemonResult>();
        yield return StartCoroutine(FetchPokemonPool(requests, pool));

        if (pool.Count == 0)
        {
            Debug.LogError("Failed to load any Pokemon for the roll.");
            isRolling = false;
            yield break;
        }

        // The last successfully-loaded entry is our landing frame.
        PokemonResult finalResult = pool[pool.Count - 1];

        // 3) Cycle through the pool sprites, easing the interval from fast to slow
        //    (slot-machine style: fast start, slow finish).
        int cycleCount = pool.Count - 1; // last one is the real landing frame

        for (int i = 0; i < cycleCount; i++)
        {
            spriteRenderer.sprite = pool[i].sprite;
            PlayClip(cycleTickSound);

            float t = cycleCount > 1 ? (float)i / (cycleCount - 1) : 1f;
            float interval = Mathf.Lerp(startInterval, endInterval, t);

            yield return new WaitForSeconds(interval);
        }

        // 4) Land on the final Pokemon.
        spriteRenderer.sprite = finalResult.sprite;
        gameObject.name = finalResult.isShiny ? $"Pokemon_{finalResult.name}_Shiny" : $"Pokemon_{finalResult.name}";

        AudioClip revealClip = finalResult.isShiny
            ? (shinyLandSound != null ? shinyLandSound : landSound)
            : landSound;
        PlayClip(revealClip != null ? revealClip : cycleTickSound);

        Debug.Log(finalResult.isShiny
            ? $"✨ Landed on SHINY Pokemon: {finalResult.name} ✨"
            : $"Landed on random Pokemon: {finalResult.name}");

        isRolling = false;
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private IEnumerator FetchPokemonPool(List<PokemonRequest> requests, List<PokemonResult> results)
    {
        // Kick off all data+sprite fetches in parallel, storing results by index
        // so ordering (and the guaranteed-last final entry) is preserved.
        PokemonResult[] ordered = new PokemonResult[requests.Count];
        List<Coroutine> running = new List<Coroutine>();

        for (int i = 0; i < requests.Count; i++)
        {
            int index = i;
            running.Add(StartCoroutine(FetchSinglePokemon(requests[index], r => ordered[index] = r)));
        }

        foreach (Coroutine c in running)
        {
            yield return c;
        }

        foreach (PokemonResult r in ordered)
        {
            if (r != null)
            {
                results.Add(r);
            }
        }
    }

    private IEnumerator FetchSinglePokemon(PokemonRequest request, System.Action<PokemonResult> onComplete)
    {
        string dataUrl = $"https://pokeapi.co/api/v2/pokemon/{request.id}";

        using (UnityWebRequest dataRequest = UnityWebRequest.Get(dataUrl))
        {
            yield return dataRequest.SendWebRequest();

            if (dataRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"Failed to fetch Pokemon data for id {request.id}: {dataRequest.error}");
                onComplete(null);
                yield break;
            }

            PokemonData data = JsonUtility.FromJson<PokemonData>(dataRequest.downloadHandler.text);
            string spriteUrl = request.isShiny ? data.sprites.front_shiny : data.sprites.front_default;

            // Fall back to the normal sprite if a shiny variant isn't available for some reason.
            if (string.IsNullOrEmpty(spriteUrl))
            {
                spriteUrl = data.sprites.front_default;
            }

            if (string.IsNullOrEmpty(spriteUrl))
            {
                Debug.LogWarning($"Pokemon id {request.id} has no usable sprite.");
                onComplete(null);
                yield break;
            }

            using (UnityWebRequest textureRequest = UnityWebRequestTexture.GetTexture(spriteUrl))
            {
                yield return textureRequest.SendWebRequest();

                if (textureRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Failed to download sprite for {data.name}: {textureRequest.error}");
                    onComplete(null);
                    yield break;
                }

                Texture2D texture = DownloadHandlerTexture.GetContent(textureRequest);
                texture.filterMode = FilterMode.Point;

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );

                onComplete(new PokemonResult { name = data.name, sprite = sprite, isShiny = request.isShiny });
            }
        }
    }

    private struct PokemonRequest
    {
        public int id;
        public bool isShiny;

        public PokemonRequest(int id, bool isShiny)
        {
            this.id = id;
            this.isShiny = isShiny;
        }
    }

    private class PokemonResult
    {
        public string name;
        public Sprite sprite;
        public bool isShiny;
    }

    // --- Minimal data classes matching only the JSON fields we need ---
    [System.Serializable]
    private class PokemonData
    {
        public string name;
        public Sprites sprites;
    }

    [System.Serializable]
    private class Sprites
    {
        public string front_default;
        public string front_shiny;
    }
}