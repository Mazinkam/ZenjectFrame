using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace Frame.Settings
{
    public interface IApplicationSettings
    {
        IAudioManager AudioManager { get; }
        ILanguageManager LanguageManager  { get; }
    }

    public class ApplicationSettings : IApplicationSettings,  IInitializable
    {
        public IAudioManager AudioManager { get; private set; }
        public ILanguageManager LanguageManager { get; private set; }

        public ApplicationSettings(IAudioManager AudioManager, ILanguageManager LanguageManager)
        {
            this.AudioManager = AudioManager;
            this.LanguageManager = LanguageManager;
        }

        public void Initialize()
        {
            Debug.Log("ApplicationSettings Initialized");
        }
    }
}
