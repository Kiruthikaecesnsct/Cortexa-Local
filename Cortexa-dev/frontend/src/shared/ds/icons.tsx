/* Cortexa icon set — stroke icons sampled from the v2 design.
   All inherit currentColor and size via the `size` prop. */
import { type ReactNode } from 'react';

interface IconProps {
  size?: number;
}

function svg(children: ReactNode, size = 18) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
      {children}
    </svg>
  );
}

export const IconDashboard = ({ size }: IconProps) =>
  svg(
    <>
      <rect x="3" y="3" width="7" height="7" rx="1.5" />
      <rect x="14" y="3" width="7" height="7" rx="1.5" />
      <rect x="3" y="14" width="7" height="7" rx="1.5" />
      <rect x="14" y="14" width="7" height="7" rx="1.5" />
    </>,
    size
  );

export const IconUpload = ({ size }: IconProps) =>
  svg(<path d="M12 16V4M12 4l-5 5M12 4l5 5M4 20h16" strokeLinecap="round" strokeLinejoin="round" />, size);

export const IconHistory = ({ size }: IconProps) =>
  svg(<path d="M3 12a9 9 0 1 0 3-6.7M3 4v5h5M12 8v5l3 2" strokeLinecap="round" strokeLinejoin="round" />, size);

export const IconUser = ({ size }: IconProps) =>
  svg(
    <>
      <circle cx="12" cy="8" r="4" />
      <path d="M4 20c0-4 4-6 8-6s8 2 8 6" strokeLinecap="round" />
    </>,
    size
  );

export const IconSettings = ({ size }: IconProps) =>
  svg(
    <>
      <circle cx="12" cy="12" r="3" />
      <path
        d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </>,
    size
  );

export const IconUsers = ({ size }: IconProps) =>
  svg(
    <>
      <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" strokeLinecap="round" />
      <circle cx="9" cy="7" r="4" />
      <path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" strokeLinecap="round" />
    </>,
    size
  );

export const IconShield = ({ size }: IconProps) =>
  svg(<path d="M12 2 4 6v6c0 5 3.5 8.5 8 10 4.5-1.5 8-5 8-10V6Z" strokeLinejoin="round" />, size);

export const IconAudit = ({ size }: IconProps) =>
  svg(
    <>
      <path d="M6 3h9l5 5v13H6z" strokeLinejoin="round" />
      <path d="M9 12h6M9 16h6" />
    </>,
    size
  );

export const IconLogout = ({ size }: IconProps) =>
  svg(<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" strokeLinecap="round" strokeLinejoin="round" />, size);

export const IconGit = ({ size }: IconProps) =>
  svg(
    <path
      d="M6 3v18M6 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm0 12a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm12-9a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm-6 6c3.3 0 6-2.7 6-6"
      strokeLinecap="round"
      strokeLinejoin="round"
    />,
    size
  );

export const IconFolder = ({ size }: IconProps) =>
  svg(<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" strokeLinejoin="round" />, size);

export const IconChevronRight = ({ size }: IconProps) =>
  svg(<path d="M9 6l6 6-6 6" strokeLinecap="round" strokeLinejoin="round" />, size);

export const IconCheck = ({ size }: IconProps) =>
  svg(<path d="M20 6 9 17l-5-5" strokeLinecap="round" strokeLinejoin="round" />, size);

export const IconAlertTriangle = ({ size }: IconProps) =>
  svg(
    <path
      d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"
      strokeLinecap="round"
      strokeLinejoin="round"
    />,
    size
  );

export const IconAlertCircle = ({ size }: IconProps) =>
  svg(
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 8v5M12 16h.01" strokeLinecap="round" />
    </>,
    size
  );

export const IconFile = ({ size }: IconProps) =>
  svg(<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8Zm0 0v5h5" strokeLinecap="round" strokeLinejoin="round" />, size);

export const IconScan = ({ size }: IconProps) =>
  svg(<path d="M4 8V6a2 2 0 0 1 2-2h2M16 4h2a2 2 0 0 1 2 2v2M20 16v2a2 2 0 0 1-2 2h-2M8 20H6a2 2 0 0 1-2-2v-2M4 12h16" strokeLinecap="round" strokeLinejoin="round" />, size);

export const IconServer = ({ size }: IconProps) =>
  svg(
    <>
      <rect x="3" y="4" width="18" height="7" rx="1.5" />
      <rect x="3" y="13" width="18" height="7" rx="1.5" />
      <path d="M7 7.5h.01M7 16.5h.01" strokeLinecap="round" />
    </>,
    size
  );

export const IconSearch = ({ size }: IconProps) =>
  svg(
    <>
      <circle cx="11" cy="11" r="7" />
      <path d="m20 20-3.5-3.5" strokeLinecap="round" />
    </>,
    size
  );

export const IconLock = ({ size }: IconProps) =>
  svg(
    <>
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" strokeLinecap="round" />
    </>,
    size
  );

export const IconBranch = ({ size }: IconProps) =>
  svg(
    <path
      d="M6 3v12M6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm12-12a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm0 0a9 9 0 0 1-9 9"
      strokeLinecap="round"
      strokeLinejoin="round"
    />,
    size
  );

export const IconEye = ({ size }: IconProps) =>
  svg(
    <>
      <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z" strokeLinejoin="round" />
      <circle cx="12" cy="12" r="3" />
    </>,
    size
  );

export const IconMinus = ({ size }: IconProps) => svg(<path d="M5 12h14" strokeLinecap="round" />, size);
