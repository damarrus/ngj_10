using TMPro;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>HUD label showing which flight model is active (T toggles it).</summary>
    public class FlightModelIndicator : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;

        private IcarusController _player;
        private FlightModel _last = (FlightModel)(-1);

        private void Start()
        {
#if !UNITY_EDITOR
            // Debug HUD label — visible only in the editor, hidden in builds.
            gameObject.SetActive(false);
            return;
#else
            _player = FindAnyObjectByType<IcarusController>();
#endif
        }

        private void Update()
        {
            if (_player == null || _text == null || _player.Model == _last)
                return;
            _last = _player.Model;
            bool field = _last == FlightModel.Field;
            _text.text = field ? "NEW — поле потоков  [T]" : "OLD — рельсы  [T]";
            _text.color = field
                ? new Color(0.5f, 1f, 0.6f, 0.85f)
                : new Color(1f, 0.7f, 0.4f, 0.85f);
        }
    }
}
