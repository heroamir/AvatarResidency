using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioController : MonoBehaviour
{
    public enum MicrophoneQuality { VERYLOW, HIGH, VERYHIGH }
    //Audio source with serialized field for networking 
    [SerializeField] private AudioSource source; public AudioClip myClip;
    //Key to use for push to talk functionality 
    public KeyCode pushToTalkKey;
    //So we can know when the player is talking 
    private bool isPlaying;
    void Start()
    {
        //source.clip = myClip; 
        print(Microphone.devices[0].ToString());
        //Set isn't playing at start 
        this.isPlaying = false;
        //Get the audio source source = GetComponent(); 
    }
    void FixedUpdate()
    {
        //If the push to talk key is pressed down, it usese the mic and returns
        if (Input.GetKeyDown(pushToTalkKey))
        {
            print("start");
            //source.Play(); 
            this.UseMic(true, MicrophoneQuality.HIGH); return;
        }
        //If the key is pushed up, it stops recording voice and returns 
        if (Input.GetKeyUp(pushToTalkKey))
        {
            print("end");
            this.UseMic(false, MicrophoneQuality.HIGH); return;
        }
    }
    //Method to use the microphone and play the sound 
    public void UseMic(bool useMic, MicrophoneQuality qual)
    {
        //Get the sampling rate from an enum value 
        int samplingRate = 44100;
        if (qual == MicrophoneQuality.VERYLOW)
        {
            samplingRate = 8000;
        }
        else if (qual == MicrophoneQuality.HIGH)
        {
            samplingRate = 44100;
        }
        else if (qual == MicrophoneQuality.VERYHIGH) { samplingRate = 48000; }
        //Play if we're using our mic 
        if (useMic)
        {
            //Tell the script we're using the mic
            this.isPlaying = true;
            //Get the clip from the mic from the default device and continue to 'loop' it while it's played 
            source.clip = Microphone.Start(Microphone.devices[0].ToString(), true, 1, samplingRate);
            //Make sure we're looping the source 
            source.loop = true;

            //While the microphone is active, play the audioclip
            while (!(Microphone.GetPosition(null) > 0)) { source.Play(); }
        }
        else
        {
            //If we're not using our mic make sure we're not playing 
            this.isPlaying = false;
            //Make sure that the audio clip is stopped 
            source.Stop(); //Make sure the clip is null again 
            source.clip = null;
        }
    }
}