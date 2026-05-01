using UnityEngine;

namespace FishNet.Component.Utility
{
    /// <summary>
    /// Add to any object to display current FPS.
    /// </summary>
    [AddComponentMenu("FishNet/Component/FPSDisplay")]
    public class FPSDisplay : MonoBehaviour
    {
        #region Types.
        private enum Corner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }
        #endregion

        #region Serialized.
        /// <summary>
        /// Color for text.
        /// </summary>
        [Tooltip("Color for text.")]
        [SerializeField]
        private Color _color = Color.white;
        /// <summary>
        /// Which corner to display FPS in.
        /// </summary>
        [Tooltip("Which corner to display FPS in.")]
        [SerializeField]
        private Corner _placement = Corner.TopRight;
        /// <summary>
        /// How often to update the FPS value in seconds.
        /// </summary>
        [Tooltip("How often to update the FPS value in seconds.")]
        [SerializeField]
        private float _updateInterval = 0.5f;
        #endregion

        #region Private.
        /// <summary>
        /// Style for drawn FPS.
        /// </summary>
        private GUIStyle _style = new();
        /// <summary>
        /// Current FPS value.
        /// </summary>
        private float _fps;
        /// <summary>
        /// Frames accumulated in current interval.
        /// </summary>
        private int _framesAccumulated;
        /// <summary>
        /// Time left until next update.
        /// </summary>
        private float _timeLeft;
        #endregion

        private void Start()
        {
            _timeLeft = _updateInterval;
        }

        private void Update()
        {
            _timeLeft -= Time.deltaTime;
            _framesAccumulated++;

            if (_timeLeft <= 0.0f)
            {
                _fps = _framesAccumulated / (_updateInterval - _timeLeft);
                _timeLeft = _updateInterval;
                _framesAccumulated = 0;
            }
        }

        private void OnGUI()
        {
            _style.normal.textColor = _color;
            _style.fontSize = 15;
            float width = 85f;
            float height = 15f;
            float edge = 10f;

            float horizontal;
            float vertical;

            if (_placement == Corner.TopLeft)
            {
                horizontal = 10f;
                vertical = 10f;
            }
            else if (_placement == Corner.TopRight)
            {
                horizontal = Screen.width - width - edge;
                vertical = 10f;
            }
            else if (_placement == Corner.BottomLeft)
            {
                horizontal = 10f;
                vertical = Screen.height - height - edge;
            }
            else
            {
                horizontal = Screen.width - width - edge;
                vertical = Screen.height - height - edge;
            }

            GUI.Label(new Rect(horizontal, vertical, width, height), $"FPS: {_fps:F0}", _style);
        }
    }
}