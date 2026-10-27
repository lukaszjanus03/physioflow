using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnatomicalRegionInfos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anatomical_region_infos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    function = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    common_issues = table.Column<string[]>(type: "text[]", nullable: false),
                    examination_tips = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anatomical_region_infos", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "anatomical_region_infos",
                columns: new[] { "id", "common_issues", "created_at", "created_by_id", "description", "examination_tips", "function", "name", "region_id", "updated_at", "updated_by_id" },
                values: new object[,]
                {
                    { new Guid("182af294-b3e6-ba00-afac-f4bbe2aa1c5a"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica szyi.", new string[0], "Mięśnie karku uczestniczą w utrzymywaniu i poruszaniu głową.", "Kark", "back-neck", null, null },
                    { new Guid("20c29070-ed06-dadd-6a42-8511124252fb"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśni pośladkowych.", new string[0], "Mięśnie pośladkowe uczestniczą w ruchach biodra i stabilizacji miednicy.", "Lewy pośladek", "back-left-glute", null, null },
                    { new Guid("285ba7da-9891-9055-6742-7afca0c34daa"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica barku, obejmująca między innymi mięsień naramienny.", new string[0], "Mięśnie barku uczestniczą w unoszeniu i ustawianiu ramienia.", "Prawy bark", "front-right-shoulder", null, null },
                    { new Guid("3353cbf1-ea9d-c95b-83b3-693dcc21b0e6"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Dłoniowa okolica ręki.", new string[0], "Mięśnie i ścięgna ręki umożliwiają chwyt i ruchy palców.", "Lewa dłoń", "front-left-hand", null, null },
                    { new Guid("4488b376-8514-84b1-2d71-350a03a63acb"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia dwugłowego ramienia.", new string[0], "Mięsień zgina staw łokciowy i odwraca przedramię.", "Prawy biceps", "front-right-biceps", null, null },
                    { new Guid("49c37b1a-0fb9-cc44-3ecf-618ae0ebf8c6"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica przedramienia.", new string[0], "Mięśnie tej okolicy uczestniczą w prostowaniu nadgarstka i palców.", "Prawe przedramię tylne", "back-right-forearm", null, null },
                    { new Guid("4b3bd46e-c336-565e-de6a-0f1aeda1b232"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica uda, obejmująca między innymi mięsień czworogłowy.", new string[0], "Mięsień czworogłowy prostuje staw kolanowy.", "Lewe udo przednie", "front-left-thigh", null, null },
                    { new Guid("4c9132fa-4a1b-c599-ea15-af60dccad36b"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Stopa widoczna od przodu.", new string[0], "Stopa stanowi podporę i uczestniczy w chodzie.", "Prawa stopa", "front-right-foot", null, null },
                    { new Guid("54920e48-d092-9ce5-3e2c-891848db1c4d"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Dłoniowa okolica ręki.", new string[0], "Mięśnie i ścięgna ręki umożliwiają chwyt i ruchy palców.", "Prawa dłoń", "front-right-hand", null, null },
                    { new Guid("5d066f1c-c914-ded3-b91f-9d669dc539b5"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica stawu kolanowego.", new string[0], "Staw kolanowy umożliwia zginanie i prostowanie kończyny.", "Lewe kolano", "front-left-knee", null, null },
                    { new Guid("65f32065-5c9f-7bc8-701b-5b64fafd09f3"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia czworobocznego.", new string[0], "Mięsień uczestniczy w ustawianiu i stabilizacji łopatki.", "Prawy czworoboczny", "back-right-trapezius", null, null },
                    { new Guid("68b6a87e-98a2-8580-710d-80b5ef7b8794"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Grzbietowa okolica ręki.", new string[0], "Ścięgna prostowników uczestniczą w prostowaniu palców.", "Lewa dłoń", "back-left-hand", null, null },
                    { new Guid("6a59e43e-7e3e-6938-ccae-763530eeb811"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Górna część przedniej ściany brzucha.", new string[0], "Mięśnie brzucha uczestniczą w ruchach i stabilizacji tułowia.", "Brzuch górny", "front-upper-abdomen", null, null },
                    { new Guid("6caf53fc-957b-15ec-aa5e-113b646ba273"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica łopatki i otaczających ją mięśni.", new string[0], "Łopatka stanowi ruchomą podstawę dla pracy kończyny górnej.", "Prawa łopatka", "back-right-scapula", null, null },
                    { new Guid("6db6f451-88e9-0666-469d-3758e581845f"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia czworobocznego.", new string[0], "Mięsień uczestniczy w ustawianiu i stabilizacji łopatki.", "Lewy czworoboczny", "back-left-trapezius", null, null },
                    { new Guid("79614cf0-33d7-81ff-1109-86394a3a624f"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica łopatki i otaczających ją mięśni.", new string[0], "Łopatka stanowi ruchomą podstawę dla pracy kończyny górnej.", "Lewa łopatka", "back-left-scapula", null, null },
                    { new Guid("7992a53a-2b47-8719-aab4-a1b25bfb9097"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Grzbietowa okolica ręki.", new string[0], "Ścięgna prostowników uczestniczą w prostowaniu palców.", "Prawa dłoń", "back-right-hand", null, null },
                    { new Guid("7b4cedcd-362b-40c2-4e65-7a17c7d468d0"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica uda, obejmująca grupę mięśni kulszowo-goleniowych.", new string[0], "Mięśnie tej grupy uczestniczą w zginaniu kolana i prostowaniu biodra.", "Lewe udo tylne", "back-left-hamstring", null, null },
                    { new Guid("7f4c75ab-31d2-cc03-7659-5f984a4c1ba8"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica podudzia.", new string[0], "Mięśnie przedniej grupy podudzia uczestniczą w zgięciu grzbietowym stopy.", "Prawa goleń", "front-right-shin", null, null },
                    { new Guid("82367d9e-7af2-d05c-8322-c52ef7007b2b"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia piersiowego większego.", new string[0], "Mięsień uczestniczy w przywodzeniu i rotacji wewnętrznej ramienia.", "Prawy mięsień piersiowy", "front-right-pec", null, null },
                    { new Guid("82524e7a-606a-4285-40f1-7f7ffa67f461"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśni pośladkowych.", new string[0], "Mięśnie pośladkowe uczestniczą w ruchach biodra i stabilizacji miednicy.", "Prawy pośladek", "back-right-glute", null, null },
                    { new Guid("9669ea70-7fc7-5f16-7a9a-701ac574a2bb"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Stopa widoczna od tyłu, w tym okolica pięty.", new string[0], "Stopa stanowi podporę i uczestniczy w przenoszeniu obciążeń.", "Prawa stopa", "back-right-foot", null, null },
                    { new Guid("96c5d9b0-47c7-3966-ff2f-09aef6b451f7"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica podudzia, obejmująca mięśnie brzuchaty łydki i płaszczkowaty.", new string[0], "Mięśnie łydki uczestniczą w zgięciu podeszwowym stopy.", "Prawa łydka", "back-right-calf", null, null },
                    { new Guid("9ff90b66-b4de-ca0b-6bcc-547c8c75ef21"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica przedramienia.", new string[0], "Mięśnie przedramienia uczestniczą w ruchach nadgarstka i palców.", "Prawe przedramię", "front-right-forearm", null, null },
                    { new Guid("aa98a8fc-7262-6e67-cb41-a47e8914f9f2"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica barku, obejmująca między innymi mięsień naramienny.", new string[0], "Mięśnie barku uczestniczą w unoszeniu i ustawianiu ramienia.", "Lewy bark", "front-left-shoulder", null, null },
                    { new Guid("ad1d45cc-7fb6-be97-fced-ac00b09dd761"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Dolna część przedniej ściany brzucha.", new string[0], "Mięśnie brzucha współtworzą ścianę brzucha i stabilizują tułów.", "Brzuch dolny", "front-lower-abdomen", null, null },
                    { new Guid("ad6bccaa-1dd1-9491-29a3-1eb713f7dc4d"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica uda, obejmująca grupę mięśni kulszowo-goleniowych.", new string[0], "Mięśnie tej grupy uczestniczą w zginaniu kolana i prostowaniu biodra.", "Prawe udo tylne", "back-right-hamstring", null, null },
                    { new Guid("b57b54ff-9fc3-c078-cb5f-6d2f50f1e644"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica stawu kolanowego.", new string[0], "Jest obszarem przebiegu ścięgien mięśni poruszających kolanem.", "Prawe kolano tylne", "back-right-knee", null, null },
                    { new Guid("b8cc19b3-c3e5-07dd-d907-c71dbb44c422"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Środkowa okolica przedniej ściany klatki piersiowej.", new string[0], "Mostek jest elementem kostnym klatki piersiowej, nie mięśniem.", "Mostek", "front-sternum", null, null },
                    { new Guid("bd3b28c7-311a-7055-8b6b-bd71fc0d698a"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica barku.", new string[0], "Mięśnie tego obszaru uczestniczą w ruchach i stabilizacji ramienia.", "Lewy bark tylny", "back-left-rear-shoulder", null, null },
                    { new Guid("be56a4f5-c8bb-fde8-7d40-47127e2a258b"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica stawu kolanowego.", new string[0], "Staw kolanowy umożliwia zginanie i prostowanie kończyny.", "Prawe kolano", "front-right-knee", null, null },
                    { new Guid("c0a06f8f-3d1e-026b-f3a9-ae159a920f45"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia trójgłowego ramienia.", new string[0], "Mięsień prostuje staw łokciowy.", "Lewy triceps", "back-left-triceps", null, null },
                    { new Guid("c3181313-b467-a8d2-87d3-7f38dd5eda2d"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica podudzia.", new string[0], "Mięśnie przedniej grupy podudzia uczestniczą w zgięciu grzbietowym stopy.", "Lewa goleń", "front-left-shin", null, null },
                    { new Guid("cc22b0d7-bbf2-2d6b-9dd7-cf7909040423"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica lędźwiowego odcinka kręgosłupa.", new string[0], "Uczestniczy w podparciu i ruchach tułowia.", "Odcinek lędźwiowy", "back-lumbar-spine", null, null },
                    { new Guid("ce016d2e-3f73-5ba7-6c16-ae40467e234b"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica barku.", new string[0], "Mięśnie tego obszaru uczestniczą w ruchach i stabilizacji ramienia.", "Prawy bark tylny", "back-right-rear-shoulder", null, null },
                    { new Guid("ce9436e7-a923-a4cb-a951-a7d313d7ca76"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia trójgłowego ramienia.", new string[0], "Mięsień prostuje staw łokciowy.", "Prawy triceps", "back-right-triceps", null, null },
                    { new Guid("d10ae043-f4b3-57ca-c7d0-1b7d374604d5"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica przedramienia.", new string[0], "Mięśnie przedramienia uczestniczą w ruchach nadgarstka i palców.", "Lewe przedramię", "front-left-forearm", null, null },
                    { new Guid("d4586043-892f-7bf9-1c97-130fb693215f"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica stawu kolanowego.", new string[0], "Jest obszarem przebiegu ścięgien mięśni poruszających kolanem.", "Lewe kolano tylne", "back-left-knee", null, null },
                    { new Guid("dad54716-9df5-5a9d-a094-45214e8a7d93"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica przedramienia.", new string[0], "Mięśnie tej okolicy uczestniczą w prostowaniu nadgarstka i palców.", "Lewe przedramię tylne", "back-left-forearm", null, null },
                    { new Guid("ddf58b7e-9d0d-9469-d266-4fe8183bd7c6"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica szyi.", new string[0], "Mięśnie tego obszaru uczestniczą w ruchach głowy i szyi.", "Szyja", "front-neck", null, null },
                    { new Guid("e0668862-3474-b187-91be-25f8d224f161"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia piersiowego większego.", new string[0], "Mięsień uczestniczy w przywodzeniu i rotacji wewnętrznej ramienia.", "Lewy mięsień piersiowy", "front-left-pec", null, null },
                    { new Guid("e5fcf2eb-0502-e11b-7775-b2f7fa928c38"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Stopa widoczna od przodu.", new string[0], "Stopa stanowi podporę i uczestniczy w chodzie.", "Lewa stopa", "front-left-foot", null, null },
                    { new Guid("ef7ac2cc-4fa8-2d5c-ff0e-baad48e67cac"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica mięśnia dwugłowego ramienia.", new string[0], "Mięsień zgina staw łokciowy i odwraca przedramię.", "Lewy biceps", "front-left-biceps", null, null },
                    { new Guid("f29a1084-1cc9-a860-e1f4-47ac149a8ded"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Okolica piersiowego odcinka kręgosłupa.", new string[0], "Stanowi część osiowego podparcia tułowia i łączy się z żebrami.", "Odcinek piersiowy", "back-thoracic-spine", null, null },
                    { new Guid("f7f68b2d-280a-cc79-b71c-48894f9d255c"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Przednia okolica uda, obejmująca między innymi mięsień czworogłowy.", new string[0], "Mięsień czworogłowy prostuje staw kolanowy.", "Prawe udo przednie", "front-right-thigh", null, null },
                    { new Guid("fa964173-996f-2df4-c44d-60bbd1a4a8f1"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Tylna okolica podudzia, obejmująca mięśnie brzuchaty łydki i płaszczkowaty.", new string[0], "Mięśnie łydki uczestniczą w zgięciu podeszwowym stopy.", "Lewa łydka", "back-left-calf", null, null },
                    { new Guid("fadf2ec1-5c2b-ae80-172d-b4be41b1526c"), new string[0], new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Stopa widoczna od tyłu, w tym okolica pięty.", new string[0], "Stopa stanowi podporę i uczestniczy w przenoszeniu obciążeń.", "Lewa stopa", "back-left-foot", null, null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_anatomical_region_infos_region_id",
                table: "anatomical_region_infos",
                column: "region_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anatomical_region_infos");
        }
    }
}
