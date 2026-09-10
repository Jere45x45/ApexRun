using TMPro;
using UnityEngine;

public class RaceCountdownUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private RaceManager raceManager;

    [SerializeField]
    private TMP_Text countdownText;

    [Header("Visual")]
    [SerializeField]
    [Min(0f)]
    private float goDisplayDuration = 1f;

    [SerializeField]
    [Min(0f)]
    private float goFadeDuration = 0.5f;

    private float goTimer;
    private bool fadingGo;

    private void OnEnable()
    {
        if (raceManager != null)
        {
            raceManager.StateChanged +=
                HandleStateChanged;
        }
    }

    private void OnDisable()
    {
        if (raceManager != null)
        {
            raceManager.StateChanged -=
                HandleStateChanged;
        }
    }

    private void Start()
    {
        if (raceManager == null)
        {
            Debug.LogError(
                "RaceCountdownUI necesita un RaceManager.",
                this
            );

            return;
        }

        if (countdownText == null)
        {
            Debug.LogError(
                "RaceCountdownUI necesita un TMP_Text.",
                this
            );

            return;
        }

        SetAlpha(0f);

        HandleStateChanged(
            raceManager.CurrentState
        );
    }

    private void Update()
    {
        if (raceManager == null ||
            countdownText == null)
        {
            return;
        }

        if (raceManager.IsCountdown())
        {
            fadingGo = false;

            SetAlpha(1f);

            UpdateCountdownText();

            return;
        }

        if (!fadingGo)
            return;

        if (goTimer > 0f)
        {
            goTimer -= Time.deltaTime;

            return;
        }

        FadeOutGo();
    }

    private void HandleStateChanged(
        RaceManager.RaceState state)
    {
        switch (state)
        {
            case RaceManager.RaceState.Waiting:

                StopGo();

                HideText();

                break;

            case RaceManager.RaceState.Countdown:

                StopGo();

                countdownText.gameObject.SetActive(
                    true
                );

                SetAlpha(1f);

                UpdateCountdownText();

                break;

            case RaceManager.RaceState.Racing:

                ShowGo();

                break;

            case RaceManager.RaceState.Finished:

                StopGo();

                HideText();

                break;
        }
    }

    private void ShowGo()
    {
        countdownText.gameObject.SetActive(
            true
        );

        countdownText.text = "GO!";

        SetAlpha(1f);

        goTimer =
            goDisplayDuration;

        fadingGo =
            true;
    }

    private void FadeOutGo()
    {
        if (goFadeDuration <= 0f)
        {
            HideText();

            fadingGo = false;

            return;
        }

        float normalizedFade =
            Mathf.Clamp01(
                -goTimer / goFadeDuration
            );

        float alpha =
            1f - normalizedFade;

        SetAlpha(alpha);

        if (alpha <= 0f)
        {
            HideText();

            fadingGo = false;
        }

        goTimer -= Time.deltaTime;
    }

    private void StopGo()
    {
        goTimer = 0f;
        fadingGo = false;
    }

    private void HideText()
    {
        SetAlpha(0f);

        countdownText.text = "";

        countdownText.gameObject.SetActive(
            false
        );
    }

    private void SetAlpha(float alpha)
    {
        Color color =
            countdownText.color;

        color.a =
            Mathf.Clamp01(alpha);

        countdownText.color =
            color;
    }

    private void UpdateCountdownText()
    {
        float time =
            raceManager.CountdownTimer;

        int number =
            Mathf.CeilToInt(time);

        countdownText.text =
            number.ToString();
    }
}