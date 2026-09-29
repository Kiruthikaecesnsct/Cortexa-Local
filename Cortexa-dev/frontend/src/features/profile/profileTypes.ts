export interface NotificationPrefs {
  batchCompletion: boolean;
  newOpportunities: boolean;
}

export interface UserProfile {
  name: string;
  email: string;
  organization: string;
  theme: 'light' | 'dark';
  notifications: NotificationPrefs;
}

export type ProfileErrorKind = 'network' | 'server' | 'not_found';

export interface ProfileError {
  kind: ProfileErrorKind;
  message: string;
}

export type FetchProfileResult =
  | { ok: true; data: UserProfile }
  | { ok: false; error: ProfileError };

export type UpdateProfileResult =
  | { ok: true; data: UserProfile }
  | { ok: false; error: ProfileError };
