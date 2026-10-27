using UnityEngine;

// Lets the client compare design options in the headset: tile scheme plus vanity choice.
// Left controller: X = next option, Y = previous option. A label shows which option is active.
public class TileOptionSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class Option
    {
        public Texture2D label;
        public Material[] materials;   // one per entry in swapRenderers
        public int vanity;             // index into vanities
        public GameObject[] show;      // which of the toggled objects this option shows
    }

    public Renderer[] swapRenderers;
    public GameObject[] vanities;
    public GameObject[] toggled;       // option-specific extras (e.g. the toilet niches in option 2)
    public Option[] options;
    public int startOption;
    public Renderer labelQuad;
    public float labelSeconds = 4f;

    int _option;
    float _hideAt;

    void Start() => Apply(startOption, true);

    void Update()
    {
        var count = options.Length;
        if (OVRInput.GetDown(OVRInput.Button.Three)) Apply((_option + 1) % count, true);
        else if (OVRInput.GetDown(OVRInput.Button.Four)) Apply((_option + count - 1) % count, true);

        if (labelQuad != null && labelQuad.enabled && Time.time > _hideAt)
            labelQuad.enabled = false;
    }

    public void Apply(int option, bool showLabel)
    {
        _option = option;
        var o = options[option];
        for (var i = 0; i < swapRenderers.Length; i++)
            swapRenderers[i].sharedMaterial = o.materials[i];
        for (var v = 0; v < vanities.Length; v++)
            vanities[v].SetActive(v == o.vanity);
        foreach (var t in toggled)
            t.SetActive(System.Array.IndexOf(o.show, t) >= 0);

        if (!showLabel || labelQuad == null) return;
        var rig = FindAnyObjectByType<OVRCameraRig>();
        var head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main?.transform;
        if (head != null)
        {
            labelQuad.transform.SetParent(head, false);
            labelQuad.transform.localPosition = new Vector3(0f, -0.15f, 0.9f);
            labelQuad.transform.localRotation = Quaternion.identity;
        }
        labelQuad.material.SetTexture("_BaseMap", o.label);
        labelQuad.enabled = true;
        _hideAt = Time.time + labelSeconds;
    }
}
