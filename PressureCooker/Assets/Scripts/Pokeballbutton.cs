using UnityEngine;
using UnityEngine.UI;

// Attach this to each of your 6 pokeball GameObjects (the ones with
// SpriteRenderer + Button already on them).
[RequireComponent(typeof(SpriteRenderer))]
public class PokeballButton : MonoBehaviour
{
    [Header("State")]
    [Tooltip("True once this pokeball already has a Pokemon stored in it.")]
    [SerializeField] private bool isFilled = false;

    [Header("Audio (optional)")]
    [Tooltip("Played when this pokeball successfully catches a Pokemon.")]
    [SerializeField] private AudioClip catchSound;

    [Tooltip("Played if you click this pokeball but no Pokemon is ready yet, or it's already full.")]
    [SerializeField] private AudioClip deniedSound;

    private SpriteRenderer spriteRenderer;
    private Sprite emptyPokeballSprite;
    private Button button;
    private AudioSource audioSource;
    private Collider2D col; // Reference to the object's collider

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        emptyPokeballSprite = spriteRenderer.sprite; // remember the original closed-pokeball art

        col = GetComponent<Collider2D>(); // Caches BoxCollider2D, CircleCollider2D, etc.

        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnPokeballClicked);
        }
        else
        {
            Debug.LogWarning($"{name} has no Button component — clicking won't be detected.");
        }

        // Reuse an existing AudioSource if present, otherwise add one for the catch sounds.
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
    }

    private void OnPokeballClicked()
    {
        if (isFilled)
        {
            Debug.Log($"{name} already has a Pokemon inside it.");
            PlayClip(deniedSound);
            return;
        }

        if (PokemonRandomSprite.Instance == null)
        {
            Debug.LogWarning("No PokemonRandomSprite (slot machine) found in the scene.");
            return;
        }

        if (PokemonRandomSprite.Instance.TryClaimPokemon(out Sprite caughtSprite, out string caughtName, out bool isShiny))
        {
            spriteRenderer.sprite = caughtSprite;
            isFilled = true;
            gameObject.name = isShiny ? $"Pokeball_{caughtName}_Shiny" : $"Pokeball_{caughtName}";
            transform.localScale = new Vector3(3f, 3f, 3f);

            // Permanently disable the collider upon successfully catching a Pokemon
            if (col != null)
            {
                col.enabled = false;
            }

            PlayClip(catchSound);
            Debug.Log($"Caught {caughtName} in {name}!");
        }
        else
        {
            // Slot machine hasn't landed on anything yet (or it was just claimed by another ball).
            Debug.Log("No Pokemon ready yet — wait for the slot machine to land on one.");
            PlayClip(deniedSound);
        }
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    /// <summary>
    /// Optional helper — empties this pokeball back to its original closed sprite.
    /// Wire this up to a "release" button or call it when resetting the scene.
    /// </summary>
    public void ResetPokeball()
    {
        spriteRenderer.sprite = emptyPokeballSprite;
        isFilled = false;
        gameObject.name = "Pokeball";

        // Re-enable the collider if the ball is reset
        if (col != null)
        {
            col.enabled = true;
        }
    }
}