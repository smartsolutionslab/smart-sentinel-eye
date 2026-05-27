using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLayoutOverlayBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "overlay_id",
                table: "layout_revisions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "overlay_id",
                table: "layout_revisions");
        }
    }
}
