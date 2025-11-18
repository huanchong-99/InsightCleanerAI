using System;
using System.Security.Principal;

namespace InsightCleanerAI.Infrastructure
{
    /// <summary>
    /// 管理员权限检测工具类
    /// </summary>
    public static class AdminHelper
    {
        /// <summary>
        /// 检查当前进程是否以管理员身份运行
        /// </summary>
        /// <returns>如果是管理员返回true，否则返回false</returns>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
