using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// مرحلهٔ هفدهم (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را ندارد): ستون Users.SystemAdminPhone.
    /// <para>آخرین شمارهٔ تنظیمات سرور (Shop:DefaultAdmins) که برای هر مدیر اصلی اعمال شده. با آن، اصلاح شمارهٔ داخل
    /// تنظیمات (مثلاً 09013181090 ← 09913181090) در اجرای بعدی اعمال می‌شود؛ ولی تغییر شماره از پنل دست نمی‌خورد.</para>
    /// <para>دادهٔ فعلی: برای مدیران اصلی موجود، شمارهٔ فعلی حسابشان به‌عنوان «آخرین شمارهٔ اعمال‌شده» ثبت می‌شود؛
    /// پس اگر شمارهٔ تنظیمات با شمارهٔ حساب فرق داشته باشد، یعنی صاحب سرور آن را اصلاح کرده و در اولین اجرا اعمال می‌شود.</para>
    /// </summary>
    public partial class Round17ProductionFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemAdminPhone",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("UPDATE [Users] SET [SystemAdminPhone] = [PhoneNumber] WHERE [SystemAdminKey] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SystemAdminPhone",
                table: "Users");
        }
    }
}
