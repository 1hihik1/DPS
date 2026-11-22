using System;
using UnityEngine;

namespace DPS
{
    /// <summary>One breathalyzer reading produced for a driver.</summary>
    [Serializable]
    public struct BreathResult
    {
        public bool isDrunk;
        public float ppm;

        public string Display => isDrunk ? ppm.ToString("0.00") + " \u2030" : "0.00 \u2030";
    }

    /// <summary>
    /// Immutable-per-spawn description of one car: appearance, plate and the offences the player
    /// must catch. Created by <see cref="ShiftManager"/> so the wanted board and the car agree.
    /// </summary>
    [Serializable]
    public class CarData
    {
        public string plate;
        public Color bodyColor = Color.gray;

        public bool isDrunk;
        public bool offersBribe;
        public bool hasContraband;
        public string contrabandLabel = "\u041A\u041E\u041D\u0422\u0420\u0410\u0411\u0410\u041D\u0414\u0410";

        /// <summary>True when this plate appears on the wanted board.</summary>
        public bool isWanted;

        public string crimeDescription = "\u041D\u0410\u0420\u0423\u0428\u0415\u041D\u0418\u0419 \u041D\u0415\u0422";

        public string driverLine = "\u0414\u043E\u0431\u0440\u044B\u0439 \u0432\u0435\u0447\u0435\u0440, \u043A\u043E\u043C\u0430\u043D\u0434\u0438\u0440.";

        /// <summary>Rolled once so the breathalyzer reads the same value every time it is used.</summary>
        public BreathResult breath;

        /// <summary>A car is a violator if any offence is present.</summary>
        public bool HasViolation => isDrunk || isWanted || hasContraband;

        /// <summary>Why the car should be detained, for the HUD and summary.</summary>
        public string ViolationSummary
        {
            get
            {
                if (isWanted) return crimeDescription;
                if (hasContraband) return "\u041A\u041E\u041D\u0422\u0420\u0410\u0411\u0410\u041D\u0414\u0410";
                if (isDrunk) return "\u0410\u041B\u041A\u041E\u0413\u041E\u041B\u042C";
                return "\u041D\u0410\u0420\u0423\u0428\u0415\u041D\u0418\u0419 \u041D\u0415\u0422";
            }
        }

        /// <summary>
        /// Builds a random car. Pass <paramref name="isWantedCar"/> true for exactly one car in a
        /// shift, together with the plate shown on the wanted board.
        /// </summary>
        public static CarData Create(int index, bool isWantedCar, string wantedPlate, System.Random rng)
        {
            var data = new CarData
            {
                plate = isWantedCar && !string.IsNullOrEmpty(wantedPlate) ? wantedPlate : NewPlate(rng),
                bodyColor = RandomBodyColor(rng),
                isWanted = isWantedCar,
            };

            // Never let the shift consist only of violators: slot 1 is always a clean car when it
            // is not the wanted one, so "let him pass" is sometimes the right call.
            var mustBeClean = !isWantedCar && index == 1;

            data.isDrunk = !mustBeClean && rng.NextDouble() < 0.45;
            data.hasContraband = !mustBeClean && !isWantedCar && rng.NextDouble() < 0.35;
            data.offersBribe = (data.HasViolation || rng.NextDouble() < 0.2) && rng.NextDouble() < 0.5;

            if (data.isWanted)
                data.crimeDescription = RandomCrime(rng);
            else if (data.hasContraband)
                data.crimeDescription = "\u041F\u0415\u0420\u0415\u0412\u041E\u0417\u041A\u0410 \u041A\u041E\u041D\u0422\u0420\u0410\u0411\u0410\u041D\u0414\u042B";

            data.contrabandLabel = RandomContraband(rng);
            data.driverLine = RandomDriverLine(rng, data);
            data.breath = data.isDrunk
                ? new BreathResult { isDrunk = true, ppm = 1.2f + (float)rng.NextDouble() * 1.6f }
                : new BreathResult { isDrunk = false, ppm = 0f };

            return data;
        }

        static readonly string[] PlateLetters = { "\u0410", "\u0412", "\u0415", "\u041A", "\u041C", "\u041D", "\u041E", "\u0420", "\u0421", "\u0422", "\u0423", "\u0425" };

        static readonly string[] PlateRegions = { "77", "50", "99", "177", "199", "777" };

        static readonly Color[] BodyColors =
        {
            new Color(0.55f, 0.56f, 0.58f), // silver
            new Color(0.06f, 0.07f, 0.09f), // black
            new Color(0.45f, 0.05f, 0.06f), // dark red
            new Color(0.05f, 0.14f, 0.32f), // navy
            new Color(0.30f, 0.30f, 0.28f), // grey-green
        };

        /// <summary>A fresh plate in the Russian <c>A777AA 77</c> layout.</summary>
        public static string NewPlate(System.Random rng)
        {
            // Russian layout: A777AA 77
            var letter1 = PlateLetters[rng.Next(PlateLetters.Length)];
            var letter2 = PlateLetters[rng.Next(PlateLetters.Length)];
            var letter3 = PlateLetters[rng.Next(PlateLetters.Length)];
            var digits = rng.Next(0, 1000).ToString("000");
            var region = PlateRegions[rng.Next(PlateRegions.Length)];
            return letter1 + digits + letter2 + letter3 + " " + region;
        }

        static Color RandomBodyColor(System.Random rng) => BodyColors[rng.Next(BodyColors.Length)];

        static readonly string[] Crimes =
        {
            "\u0423\u0413\u041E\u041D \u0422\u0420\u0410\u041D\u0421\u041F\u041E\u0420\u0422\u0410",
            "\u041F\u041E\u0414\u0414\u0415\u041B\u041A\u0410 \u041D\u041E\u041C\u0415\u0420\u041E\u0412",
            "\u0421\u041A\u0420\u042B\u0422\u0418\u0415 \u0421 \u041C\u0415\u0421\u0422\u0410 \u0414\u0422\u041F",
            "\u041D\u0415\u0423\u041F\u041B\u0410\u0422\u0410 \u0428\u0422\u0420\u0410\u0424\u041E\u0412",
        };

        static readonly string[] Contraband =
        {
            "\u041A\u041E\u041D\u0422\u0420\u0410\u0411\u0410\u041D\u0414\u0410: \u041E\u0420\u0423\u0416\u0418\u0415",
            "\u041A\u041E\u041D\u0422\u0420\u0410\u0411\u0410\u041D\u0414\u0410: \u0420\u0423\u0427\u041D\u041E\u0419 \u041C\u0415\u0414\u0412\u0415\u0414\u042C",
            "\u041A\u041E\u041D\u0422\u0420\u0410\u0411\u0410\u041D\u0414\u0410: \u041A\u0410\u0420\u0422\u041E\u0428\u041A\u0410 \u041D\u0415\u041B\u0415\u0413\u0410\u041B\u042C\u041D\u0410\u042F",
        };

        static readonly string[] DriverLines =
        {
            "\u0414\u043E\u0431\u0440\u044B\u0439 \u0432\u0435\u0447\u0435\u0440, \u043A\u043E\u043C\u0430\u043D\u0434\u0438\u0440.",
            "\u0427\u0442\u043E \u0441\u043B\u0443\u0447\u0438\u043B\u043E\u0441\u044C? \u042F \u0442\u043E\u0440\u043E\u043F\u043B\u044E\u0441\u044C!",
            "\u042F \u043D\u0438\u0447\u0435\u0433\u043E \u043D\u0435 \u043D\u0430\u0440\u0443\u0448\u0430\u043B.",
            "\u0412\u0441\u0451 \u0432 \u043F\u043E\u0440\u044F\u0434\u043A\u0435, \u043F\u0440\u043E\u0432\u0435\u0440\u044F\u0439\u0442\u0435.",
        };

        static readonly string[] BribeLines =
        {
            "\u041A\u043E\u043C\u0430\u043D\u0434\u0438\u0440, \u0434\u0430\u0432\u0430\u0439 \u0434\u043E\u0433\u043E\u0432\u043E\u0440\u0438\u043C\u0441\u044F...",
            "\u041C\u043E\u0436\u0435\u0442, \u0440\u0435\u0448\u0438\u043C \u043D\u0430 \u043C\u0435\u0441\u0442\u0435?",
        };

        static string RandomCrime(System.Random rng) => Crimes[rng.Next(Crimes.Length)];

        static string RandomContraband(System.Random rng) => Contraband[rng.Next(Contraband.Length)];

        static string RandomDriverLine(System.Random rng, CarData data)
        {
            if (data.isDrunk)
                return "\u042F \u0441\u043E\u0432\u0441\u0435\u043C \u0447\u0443\u0442\u044C-\u0447\u0443\u0442\u044C, \u0447\u0435\u0441\u0442\u043D\u043E!";

            if (data.offersBribe)
                return BribeLines[rng.Next(BribeLines.Length)];

            return DriverLines[rng.Next(DriverLines.Length)];
        }
    }
}
