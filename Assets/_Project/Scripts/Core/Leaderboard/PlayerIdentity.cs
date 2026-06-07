using System;
using UnityEngine;

namespace Ngj10.Core.Leaderboard
{
    /// <summary>
    /// The local player's stable identity for the leaderboard: a generated uid
    /// (so the player owns exactly one row, survives renames and reinstalls of the
    /// same browser profile) plus a Google-style display name ("Lazy Llama").
    /// Persisted in PlayerPrefs (WebGL-safe — maps to IndexedDB), same as the
    /// achievement engine. Static so any caller can read it without wiring.
    ///
    /// Name uniqueness is not enforced — duplicates across players are fine; the
    /// uid is what disambiguates rows on the server.
    /// </summary>
    public static class PlayerIdentity
    {
        private const string UidKey = "lb.uid";
        private const string NameKey = "lb.name";

        // Two-word "adjective + animal" pool, English, Google-doc style.
        private static readonly string[] Adjectives =
        {
            "Lazy", "Swift", "Brave", "Sneaky", "Jolly", "Grumpy", "Clever",
            "Cosmic", "Fuzzy", "Mighty", "Sleepy", "Witty", "Bouncy", "Dapper",
            "Feral", "Gentle", "Humble", "Nimble", "Plucky", "Quirky", "Rowdy",
            "Sly", "Spicy", "Turbo", "Zesty", "Curious", "Daring", "Eager",
        };

        private static readonly string[] Animals =
        {
            "Llama", "Beaver", "Otter", "Panda", "Falcon", "Badger", "Lynx",
            "Walrus", "Gecko", "Marmot", "Narwhal", "Quokka", "Raccoon", "Tapir",
            "Wombat", "Axolotl", "Capybara", "Ferret", "Hedgehog", "Koala",
            "Mongoose", "Ocelot", "Pangolin", "Platypus", "Stoat", "Vole",
        };

        /// <summary>Stable per-player id. Generated and persisted on first access.</summary>
        public static string Uid
        {
            get
            {
                var uid = PlayerPrefs.GetString(UidKey, string.Empty);
                if (string.IsNullOrEmpty(uid))
                {
                    uid = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(UidKey, uid);
                    PlayerPrefs.Save();
                }
                return uid;
            }
        }

        /// <summary>Display name. Generated on first access if none stored.</summary>
        public static string Name
        {
            get
            {
                var name = PlayerPrefs.GetString(NameKey, string.Empty);
                if (string.IsNullOrEmpty(name))
                {
                    name = GenerateName();
                    PlayerPrefs.SetString(NameKey, name);
                    PlayerPrefs.Save();
                }
                return name;
            }
        }

        /// <summary>Set a new display name. Trimmed; empty input is ignored.</summary>
        public static void Rename(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }
            PlayerPrefs.SetString(NameKey, newName.Trim());
            PlayerPrefs.Save();
        }

        private static string GenerateName()
        {
            var adjective = Adjectives[UnityEngine.Random.Range(0, Adjectives.Length)];
            var animal = Animals[UnityEngine.Random.Range(0, Animals.Length)];
            return $"{adjective} {animal}";
        }
    }
}
