using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhotosInObjectStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_CheckIn",
                table: "EquipmentLoans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_Damaged",
                table: "EquipmentLoans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoPath",
                table: "EquipmentLoans");

            migrationBuilder.RenameColumn(
                name: "DamagePhotoPath",
                table: "EquipmentLoans",
                newName: "DamagePhotoKey");

            migrationBuilder.AddColumn<string>(
                name: "DamagePhotoContentType",
                table: "EquipmentLoans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DamagePhotoSizeBytes",
                table: "EquipmentLoans",
                type: "integer",
                nullable: true);

            // Existing photos (local files) keep their name as their key; the content type follows from the extension,
            // which came from the magic bytes. The size is filled by LegacyPhotoImporter when the file is moved.
            migrationBuilder.Sql("""
                update "EquipmentLoans"
                set "DamagePhotoContentType" = case when right("DamagePhotoKey", 4) = '.png' then 'image/png' else 'image/jpeg' end
                where "DamagePhotoKey" is not null;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_CheckIn",
                table: "EquipmentLoans",
                sql: "(\"CheckedInAt\" IS NULL AND \"CheckedInById\" IS NULL AND \"ReturnCondition\" IS NULL AND \"DamageNote\" IS NULL AND \"DamagePhotoKey\" IS NULL AND NOT \"IsLateReturn\") OR (\"CheckedInAt\" IS NOT NULL AND \"CheckedInById\" IS NOT NULL AND \"ReturnCondition\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_Damaged",
                table: "EquipmentLoans",
                sql: "\"ReturnCondition\" IS DISTINCT FROM 'Damaged' OR (\"DamageNote\" IS NOT NULL AND \"DamagePhotoKey\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoContentType",
                table: "EquipmentLoans",
                sql: "\"DamagePhotoContentType\" IN ('image/jpeg', 'image/png')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoKey",
                table: "EquipmentLoans",
                sql: "\"DamagePhotoKey\" ~ '^[0-9a-f]{32}\\.(jpg|png)$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoMeta",
                table: "EquipmentLoans",
                sql: "(\"DamagePhotoKey\" IS NULL) = (\"DamagePhotoContentType\" IS NULL) AND (\"DamagePhotoSizeBytes\" IS NULL OR \"DamagePhotoKey\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoSizeBytes",
                table: "EquipmentLoans",
                sql: "\"DamagePhotoSizeBytes\" BETWEEN 1 AND 5242880");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_CheckIn",
                table: "EquipmentLoans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_Damaged",
                table: "EquipmentLoans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoContentType",
                table: "EquipmentLoans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoKey",
                table: "EquipmentLoans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoMeta",
                table: "EquipmentLoans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoSizeBytes",
                table: "EquipmentLoans");

            migrationBuilder.DropColumn(
                name: "DamagePhotoContentType",
                table: "EquipmentLoans");

            migrationBuilder.DropColumn(
                name: "DamagePhotoSizeBytes",
                table: "EquipmentLoans");

            migrationBuilder.RenameColumn(
                name: "DamagePhotoKey",
                table: "EquipmentLoans",
                newName: "DamagePhotoPath");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_CheckIn",
                table: "EquipmentLoans",
                sql: "(\"CheckedInAt\" IS NULL AND \"CheckedInById\" IS NULL AND \"ReturnCondition\" IS NULL AND \"DamageNote\" IS NULL AND \"DamagePhotoPath\" IS NULL AND NOT \"IsLateReturn\") OR (\"CheckedInAt\" IS NOT NULL AND \"CheckedInById\" IS NOT NULL AND \"ReturnCondition\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_Damaged",
                table: "EquipmentLoans",
                sql: "\"ReturnCondition\" IS DISTINCT FROM 'Damaged' OR (\"DamageNote\" IS NOT NULL AND \"DamagePhotoPath\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentLoans_DamagePhotoPath",
                table: "EquipmentLoans",
                sql: "\"DamagePhotoPath\" ~ '^[0-9a-f]{32}\\.(jpg|png)$'");
        }
    }
}
