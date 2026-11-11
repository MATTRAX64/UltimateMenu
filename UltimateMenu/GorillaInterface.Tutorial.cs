using UnityEngine;

namespace UltimateMenu
{
    // Tutoriel de premier lancement (VR) : tant que le geste d'ouverture n'a pas été réussi,
    // le panneau 3D devant le joueur montre le tutoriel au lieu du menu.
    public partial class GorillaInterface
    {
        // Valeur du profil (Profile.json) : 1 = tutoriel fait, 0 = en cours, absente = jamais lancé.
        const string PREF_TUTORIAL_DONE = "MUM_TutorialDone";

        bool _tutorial, _tutorialDone;
        float _tutorialRetryAt;
        ShakeState _tutLeftState, _tutRightState;
        float _tutLeftTimer, _tutRightTimer;

        internal bool TutorialActive => _tutorial && _open;

        // 0 = attend « bas », 1 = « bas » détecté, attend « haut ».
        internal int TutorialStep => _tutLeftState == ShakeState.WentDown || _tutRightState == ShakeState.WentDown ? 1 : 0;

        // Main affichée : celle dont le stick vient d'aller vers le bas, sinon la main configurée.
        internal bool TutorialLeft
        {
            get
            {
                if (_tutLeftState == ShakeState.WentDown) return true;
                if (_tutRightState == ShakeState.WentDown) return false;
                return MenuLeft;
            }
        }

        void LoadTutorialState()
        {
            // Pas de clé dans le profil => jamais fait => on affichera le tutoriel.
            _tutorialDone = MenuPrefs.HasKey(PREF_TUTORIAL_DONE) && MenuPrefs.GetInt(PREF_TUTORIAL_DONE, 0) == 1;
        }

        void UpdateFirstRunTutorial()
        {
            if (_tutorialDone) return;

            if (!_tutorial)
            {
                if (_open || _playerHead == null || VRRig.LocalRig == null) return;
                if (Time.unscaledTime < _tutorialRetryAt) return;
                if (MenuPrefs.GetInt("MUM_VREnabled", 1) != 1) return;
                if (!UnityEngine.XR.XRSettings.isDeviceActive) return; // joueur PC : pas de manette à montrer
                if (!MenuPrefs.HasKey(PREF_TUTORIAL_DONE)) MenuPrefs.SetInt(PREF_TUTORIAL_DONE, 0);
                _tutorial = true;
                _tutLeftState = _tutRightState = ShakeState.Neutral;
                _tutLeftTimer = _tutRightTimer = 0f;
                OpenMenu();
                _openingActive = false;
                UpdateFollow(); // Anchor at the current headset pose before the first texture is shown.
                return;
            }

            // Panneau fermé de force (trop loin, lâché, capture...) : on le remet devant le joueur un peu après.
            if (!_open) { _tutorial = false; _tutorialRetryAt = Time.unscaledTime + 1.5f; return; }

            bool leftDone = TutorialGesture(ref _tutLeftState, ref _tutLeftTimer, _debugStickL);
            bool rightDone = TutorialGesture(ref _tutRightState, ref _tutRightTimer, _debugStickR);
            if (!leftDone && !rightDone) return;

            // La main qui a réussi devient la main du menu.
            if (leftDone != MenuLeft) SetMenuLeft(leftDone);
            FinishTutorial(openMenu: true);
        }

        // Même geste que l'ouverture réelle : stick vers le bas puis vers le haut, dans le délai réglé.
        bool TutorialGesture(ref ShakeState state, ref float timer, Vector2 stick)
        {
            if (timer > 0f)
            {
                timer -= Time.unscaledDeltaTime;
                if (timer <= 0f) state = ShakeState.Neutral;
            }
            if (state == ShakeState.Neutral)
            {
                if (stick.y <= -_shakeThreshold) { state = ShakeState.WentDown; timer = _shakeWindow; }
            }
            else if (state == ShakeState.WentDown && stick.y >= _shakeThreshold)
            {
                state = ShakeState.Neutral; timer = 0f;
                return true;
            }
            return false;
        }

        void FinishTutorial(bool openMenu)
        {
            _tutorial = false; _tutorialDone = true;
            _tutLeftState = _tutRightState = ShakeState.Neutral;
            MenuPrefs.SetInt(PREF_TUTORIAL_DONE, 1);
            MenuPrefs.Save();
            if (openMenu) OpenMenu();   // le vrai menu apparaît à la place du tutoriel
            else CloseMenu();
        }
    }
}
