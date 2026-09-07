using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simpleShare_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupInvitations_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GroupInvitations_GroupId",
                table: "GroupInvitations",
                column: "GroupId",
                unique: true,
                filter: "\"IsActive\""
            );

            migrationBuilder.CreateIndex(
                name: "IX_GroupInvitations_Token",
                table: "GroupInvitations",
                column: "Token",
                unique: true
            );

            // Backfill: groups that already exist need a working link the moment
            // this ships, and the application only mints invitations for groups
            // created from here on.
            //
            // gen_random_uuid() is built into PG13+, so this needs no extension,
            // and re-packing its 16 bytes as base64url yields exactly the
            // 22-character shape InviteTokenGenerator.Generate() produces.
            //
            // GroupRole = 1 is Owner (enums are stored as int). The LATERAL join
            // is LEFT because a group could have no owner carrying a UserId, and
            // CreatedByUserId is not nullable — an empty GUID beats a missing row.
            //
            // Soft-deleted groups are included deliberately. Nothing mints a link
            // lazily any more, so a group restored after this runs would otherwise
            // never get one. The extra row is harmless while the group is deleted:
            // preview and join both resolve the group through GetGroupAsync, which
            // filters soft-deleted rows and answers group_not_found.
            migrationBuilder.Sql(
                """
                INSERT INTO "GroupInvitations"
                    ("Id", "GroupId", "Token", "CreatedByUserId",
                     "CreatedAtUtc", "ExpiresAtUtc", "IsActive", "RevokedAtUtc", "UseCount")
                SELECT
                    gen_random_uuid(),
                    g."Id",
                    rtrim(replace(replace(
                        encode(uuid_send(gen_random_uuid()), 'base64'), '+', '-'), '/', '_'), '='),
                    COALESCE(owner."UserId", '00000000-0000-0000-0000-000000000000'::uuid),
                    now(),
                    NULL,
                    TRUE,
                    NULL,
                    0
                FROM "Groups" g
                LEFT JOIN LATERAL (
                    SELECT m."UserId"
                    FROM "GroupMembers" gm
                    JOIN "Members" m ON m."Id" = gm."MemberId"
                    WHERE gm."GroupId" = g."Id"
                      AND gm."GroupRole" = 1
                      AND m."UserId" IS NOT NULL
                    LIMIT 1
                ) owner ON TRUE;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GroupInvitations");
        }
    }
}
