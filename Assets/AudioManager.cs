using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace Frame.Settings
{
    public interface IAudioManager
    {
        int volume { get; set; }
        void PlayOnce();
    }

    public class AudioManager : IAudioManager, IInitializable
    {
        public int volume { get; set; }
        
        public void Initialize()
        {
            Debug.Log("AudioManager Initialized");
        }

        public void PlayOnce()
        {
           
        }
    }
}

