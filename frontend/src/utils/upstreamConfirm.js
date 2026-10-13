/** docs/150：报工上级 soft 确认错误码（与后端 ReportService.UpstreamSoftConfirmCode 一致） */
export const UPSTREAM_SOFT_CODE = 1002

export function isUpstreamSoftConfirm(err) {
  return err?.code === UPSTREAM_SOFT_CODE || err?.softConfirm === true
}
