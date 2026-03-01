using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordDockerManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DockerContainerConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ContainerId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Game = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayerJoinPattern = table.Column<string>(type: "TEXT", nullable: true),
                    PlayerLeavePattern = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockerContainerConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiscordUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContainerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RawLogLine = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DockerContainerConfigId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerEvents_DockerContainerConfigs_DockerContainerConfigId",
                        column: x => x.DockerContainerConfigId,
                        principalTable: "DockerContainerConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissionContainers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserPermissionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DockerContainerConfigId = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissionContainers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPermissionContainers_DockerContainerConfigs_DockerContainerConfigId",
                        column: x => x.DockerContainerConfigId,
                        principalTable: "DockerContainerConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPermissionContainers_UserPermissions_UserPermissionId",
                        column: x => x.UserPermissionId,
                        principalTable: "UserPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DockerContainerConfigs_Name",
                table: "DockerContainerConfigs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvents_ContainerName_Timestamp",
                table: "PlayerEvents",
                columns: new[] { "ContainerName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEvents_DockerContainerConfigId",
                table: "PlayerEvents",
                column: "DockerContainerConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionContainers_DockerContainerConfigId",
                table: "UserPermissionContainers",
                column: "DockerContainerConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionContainers_UserPermissionId_DockerContainerConfigId",
                table: "UserPermissionContainers",
                columns: new[] { "UserPermissionId", "DockerContainerConfigId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_DiscordUserId",
                table: "UserPermissions",
                column: "DiscordUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerEvents");

            migrationBuilder.DropTable(
                name: "UserPermissionContainers");

            migrationBuilder.DropTable(
                name: "DockerContainerConfigs");

            migrationBuilder.DropTable(
                name: "UserPermissions");
        }
    }
}
