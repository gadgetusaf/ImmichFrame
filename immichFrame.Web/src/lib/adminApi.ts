// Lightweight client for the admin configuration API (cookie-authenticated, same-origin).
// Kept separate from the generated slideshow client (immichFrameApi.ts), which uses bearer auth.

export interface AdminUser {
	username: string;
}

export interface AuthResult {
	ok: boolean;
	username?: string;
	message?: string;
}

export type GeneralSettings = Record<string, unknown>;

async function readMessage(res: Response, fallback: string): Promise<string> {
	try {
		const data = (await res.json()) as { message?: string };
		return data?.message ?? fallback;
	} catch {
		return fallback;
	}
}

export async function getSetupRequired(): Promise<boolean> {
	const res = await fetch('/api/admin/setup-required', { credentials: 'include' });
	if (!res.ok) return false;
	const data = (await res.json()) as { setupRequired: boolean };
	return data.setupRequired;
}

export async function getMe(): Promise<AdminUser | null> {
	const res = await fetch('/api/admin/me', { credentials: 'include' });
	if (!res.ok) return null;
	return (await res.json()) as AdminUser;
}

async function postCredentials(path: string, username: string, password: string): Promise<AuthResult> {
	const res = await fetch(`/api/admin/${path}`, {
		method: 'POST',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ username, password })
	});
	if (res.ok) {
		const data = (await res.json()) as { username: string };
		return { ok: true, username: data.username };
	}
	return { ok: false, message: await readMessage(res, 'Request failed.') };
}

export const login = (username: string, password: string) => postCredentials('login', username, password);
export const setup = (username: string, password: string) => postCredentials('setup', username, password);

export async function logout(): Promise<void> {
	await fetch('/api/admin/logout', { method: 'POST', credentials: 'include' });
}

export async function getGeneralSettings(): Promise<GeneralSettings> {
	const res = await fetch('/api/admin/general', { credentials: 'include' });
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to load settings.'));
	return (await res.json()) as GeneralSettings;
}

export async function saveGeneralSettings(settings: GeneralSettings): Promise<GeneralSettings> {
	const res = await fetch('/api/admin/general', {
		method: 'PUT',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(settings)
	});
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to save settings.'));
	return (await res.json()) as GeneralSettings;
}

export interface Account {
	id: string;
	immichServerUrl: string;
	hasApiKey: boolean;
	apiKey?: string | null;
	showMemories: boolean;
	showFavorites: boolean;
	showArchived: boolean;
	showVideos: boolean;
	imagesFromDays: number | null;
	imagesFromDate: string | null;
	imagesUntilDate: string | null;
	albums: string[];
	excludedAlbums: string[];
	people: string[];
	tags: string[];
	rating: number | null;
}

export async function listAccounts(): Promise<Account[]> {
	const res = await fetch('/api/admin/accounts', { credentials: 'include' });
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to load accounts.'));
	return (await res.json()) as Account[];
}

export async function createAccount(account: Partial<Account>): Promise<AccountSaveResult> {
	const res = await fetch('/api/admin/accounts', {
		method: 'POST',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(account)
	});
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to create account.'));
	return (await res.json()) as AccountSaveResult;
}

export async function updateAccount(id: string, account: Partial<Account>): Promise<AccountSaveResult> {
	const res = await fetch(`/api/admin/accounts/${id}`, {
		method: 'PUT',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(account)
	});
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to update account.'));
	return (await res.json()) as AccountSaveResult;
}

export async function deleteAccount(id: string): Promise<void> {
	const res = await fetch(`/api/admin/accounts/${id}`, { method: 'DELETE', credentials: 'include' });
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to delete account.'));
}

export interface NamedId {
	id: string;
	name: string;
}

export interface BrowseResult {
	albums: NamedId[];
	people: NamedId[];
	warnings: string[];
}

export interface AccountSaveResult {
	account: Account;
	warnings: string[];
}

export interface Link {
	id: string;
	slug: string;
	name: string;
	accountId: string;
	accessPolicy: 'None' | 'Pin';
	hasPin: boolean;
	pin?: string | null;
	enabled: boolean;
	showMemories: boolean;
	showFavorites: boolean;
	showArchived: boolean;
	showVideos: boolean;
	imagesFromDays: number | null;
	imagesFromDate: string | null;
	imagesUntilDate: string | null;
	albums: string[];
	excludedAlbums: string[];
	people: string[];
	tags: string[];
	rating: number | null;
}

export async function listLinks(): Promise<Link[]> {
	const res = await fetch('/api/admin/links', { credentials: 'include' });
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to load links.'));
	return (await res.json()) as Link[];
}

export async function createLink(link: Partial<Link>): Promise<Link> {
	const res = await fetch('/api/admin/links', {
		method: 'POST',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(link)
	});
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to create link.'));
	return (await res.json()) as Link;
}

export async function updateLink(id: string, link: Partial<Link>): Promise<Link> {
	const res = await fetch(`/api/admin/links/${id}`, {
		method: 'PUT',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(link)
	});
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to update link.'));
	return (await res.json()) as Link;
}

export async function deleteLink(id: string): Promise<void> {
	const res = await fetch(`/api/admin/links/${id}`, { method: 'DELETE', credentials: 'include' });
	if (!res.ok) throw new Error(await readMessage(res, 'Failed to delete link.'));
}

export async function browseAccount(request: {
	immichServerUrl: string;
	apiKey?: string;
	accountId?: string;
}): Promise<BrowseResult> {
	const res = await fetch('/api/admin/accounts/browse', {
		method: 'POST',
		credentials: 'include',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) throw new Error(await readMessage(res, 'Could not reach the Immich server.'));
	return (await res.json()) as BrowseResult;
}
