using System.Security.Cryptography;
using System.Text;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Infrastructure.Persistence.Seeds;

public static class AnatomicalRegionInfoSeed
{
    public static AnatomicalRegionInfo[] Create()
    {
        var regions = new List<AnatomicalRegionInfo>();

        Add(
            "front-neck",
            "Szyja",
            "Przednia okolica szyi.",
            "Mięśnie tego obszaru uczestniczą w ruchach głowy i szyi.");

        AddPair(
            "front", "shoulder",
            "Lewy bark", "Prawy bark",
            "Przednia okolica barku, obejmująca między innymi mięsień naramienny.",
            "Mięśnie barku uczestniczą w unoszeniu i ustawianiu ramienia.");

        AddPair(
            "front", "pec",
            "Lewy mięsień piersiowy", "Prawy mięsień piersiowy",
            "Okolica mięśnia piersiowego większego.",
            "Mięsień uczestniczy w przywodzeniu i rotacji wewnętrznej ramienia.");

        Add(
            "front-sternum",
            "Mostek",
            "Środkowa okolica przedniej ściany klatki piersiowej.",
            "Mostek jest elementem kostnym klatki piersiowej, nie mięśniem.");

        Add(
            "front-upper-abdomen",
            "Brzuch górny",
            "Górna część przedniej ściany brzucha.",
            "Mięśnie brzucha uczestniczą w ruchach i stabilizacji tułowia.");

        Add(
            "front-lower-abdomen",
            "Brzuch dolny",
            "Dolna część przedniej ściany brzucha.",
            "Mięśnie brzucha współtworzą ścianę brzucha i stabilizują tułów.");

        AddPair(
            "front", "biceps",
            "Lewy biceps", "Prawy biceps",
            "Okolica mięśnia dwugłowego ramienia.",
            "Mięsień zgina staw łokciowy i odwraca przedramię.");

        AddPair(
            "front", "forearm",
            "Lewe przedramię", "Prawe przedramię",
            "Przednia okolica przedramienia.",
            "Mięśnie przedramienia uczestniczą w ruchach nadgarstka i palców.");

        AddPair(
            "front", "hand",
            "Lewa dłoń", "Prawa dłoń",
            "Dłoniowa okolica ręki.",
            "Mięśnie i ścięgna ręki umożliwiają chwyt i ruchy palców.");

        AddPair(
            "front", "thigh",
            "Lewe udo przednie", "Prawe udo przednie",
            "Przednia okolica uda, obejmująca między innymi mięsień czworogłowy.",
            "Mięsień czworogłowy prostuje staw kolanowy.");

        AddPair(
            "front", "knee",
            "Lewe kolano", "Prawe kolano",
            "Przednia okolica stawu kolanowego.",
            "Staw kolanowy umożliwia zginanie i prostowanie kończyny.");

        AddPair(
            "front", "shin",
            "Lewa goleń", "Prawa goleń",
            "Przednia okolica podudzia.",
            "Mięśnie przedniej grupy podudzia uczestniczą w zgięciu grzbietowym stopy.");

        AddPair(
            "front", "foot",
            "Lewa stopa", "Prawa stopa",
            "Stopa widoczna od przodu.",
            "Stopa stanowi podporę i uczestniczy w chodzie.");

        Add(
            "back-neck",
            "Kark",
            "Tylna okolica szyi.",
            "Mięśnie karku uczestniczą w utrzymywaniu i poruszaniu głową.");

        AddPair(
            "back", "trapezius",
            "Lewy czworoboczny", "Prawy czworoboczny",
            "Okolica mięśnia czworobocznego.",
            "Mięsień uczestniczy w ustawianiu i stabilizacji łopatki.");

        AddPair(
            "back", "rear-shoulder",
            "Lewy bark tylny", "Prawy bark tylny",
            "Tylna okolica barku.",
            "Mięśnie tego obszaru uczestniczą w ruchach i stabilizacji ramienia.");

        AddPair(
            "back", "scapula",
            "Lewa łopatka", "Prawa łopatka",
            "Okolica łopatki i otaczających ją mięśni.",
            "Łopatka stanowi ruchomą podstawę dla pracy kończyny górnej.");

        Add(
            "back-thoracic-spine",
            "Odcinek piersiowy",
            "Okolica piersiowego odcinka kręgosłupa.",
            "Stanowi część osiowego podparcia tułowia i łączy się z żebrami.");

        Add(
            "back-lumbar-spine",
            "Odcinek lędźwiowy",
            "Okolica lędźwiowego odcinka kręgosłupa.",
            "Uczestniczy w podparciu i ruchach tułowia.");

        AddPair(
            "back", "triceps",
            "Lewy triceps", "Prawy triceps",
            "Okolica mięśnia trójgłowego ramienia.",
            "Mięsień prostuje staw łokciowy.");

        AddPair(
            "back", "forearm",
            "Lewe przedramię tylne", "Prawe przedramię tylne",
            "Tylna okolica przedramienia.",
            "Mięśnie tej okolicy uczestniczą w prostowaniu nadgarstka i palców.");

        AddPair(
            "back", "hand",
            "Lewa dłoń", "Prawa dłoń",
            "Grzbietowa okolica ręki.",
            "Ścięgna prostowników uczestniczą w prostowaniu palców.");

        AddPair(
            "back", "glute",
            "Lewy pośladek", "Prawy pośladek",
            "Okolica mięśni pośladkowych.",
            "Mięśnie pośladkowe uczestniczą w ruchach biodra i stabilizacji miednicy.");

        AddPair(
            "back", "hamstring",
            "Lewe udo tylne", "Prawe udo tylne",
            "Tylna okolica uda, obejmująca grupę mięśni kulszowo-goleniowych.",
            "Mięśnie tej grupy uczestniczą w zginaniu kolana i prostowaniu biodra.");

        AddPair(
            "back", "knee",
            "Lewe kolano tylne", "Prawe kolano tylne",
            "Tylna okolica stawu kolanowego.",
            "Jest obszarem przebiegu ścięgien mięśni poruszających kolanem.");

        AddPair(
            "back", "calf",
            "Lewa łydka", "Prawa łydka",
            "Tylna okolica podudzia, obejmująca mięśnie brzuchaty łydki i płaszczkowaty.",
            "Mięśnie łydki uczestniczą w zgięciu podeszwowym stopy.");

        AddPair(
            "back", "foot",
            "Lewa stopa", "Prawa stopa",
            "Stopa widoczna od tyłu, w tym okolica pięty.",
            "Stopa stanowi podporę i uczestniczy w przenoszeniu obciążeń.");

        return regions.ToArray();

        void Add(
            string regionId,
            string name,
            string description,
            string function)
        {
            var hash = SHA256.HashData(
                Encoding.UTF8.GetBytes($"PhysioFlow:AnatomicalRegion:{regionId}"));

            regions.Add(new AnatomicalRegionInfo
            {
                Id = new Guid(hash.AsSpan(0, 16)),
                CreatedAt = new DateTime(
                    2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RegionId = regionId,
                Name = name,
                Description = description,
                Function = function,
                CommonIssues = Array.Empty<string>(),
                ExaminationTips = Array.Empty<string>(),
            });
        }

        void AddPair(
            string view,
            string suffix,
            string leftName,
            string rightName,
            string description,
            string function)
        {
            Add(
                $"{view}-left-{suffix}",
                leftName,
                description,
                function);

            Add(
                $"{view}-right-{suffix}",
                rightName,
                description,
                function);
        }
    }
}