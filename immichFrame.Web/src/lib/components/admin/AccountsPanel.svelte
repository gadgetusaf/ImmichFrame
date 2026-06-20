<script lang="ts">
	import { onMount } from 'svelte';
	import {
		listAccounts,
		createAccount,
		updateAccount,
		deleteAccount,
		browseAccount,
		type Account,
		type BrowseResult
	} from '$lib/adminApi';

	let accounts = $state<Account[]>([]);
	let loading = $state(true);
	let error = $state('');

	let editing = $state<Account | null>(null);
	let isNew = $state(false);
	let saving = $state(false);
	let saveError = $state('');

	// Editor draft
	let draft = $state<Partial<Account>>({});
	let albumsText = $state('');
	let excludedAlbumsText = $state('');
	let peopleText = $state('');
	let tagsText = $state('');

	let browse = $state<BrowseResult | null>(null);
	let browsing = $state(false);
	let browseError = $state('');
	let saveWarnings = $state<string[]>([]);

	const inputClass =
		'w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400';

	onMount(load);

	async function load() {
		loading = true;
		error = '';
		try {
			accounts = await listAccounts();
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load accounts.';
		} finally {
			loading = false;
		}
	}

	function startAdd() {
		draft = {
			immichServerUrl: '',
			apiKey: '',
			showMemories: false,
			showFavorites: false,
			showArchived: false,
			showVideos: false,
			imagesFromDays: null,
			imagesFromDate: null,
			imagesUntilDate: null,
			rating: null
		};
		albumsText = excludedAlbumsText = peopleText = tagsText = '';
		isNew = true;
		saveError = '';
		browse = null;
		browseError = '';
		saveWarnings = [];
		editing = {} as Account;
	}

	function startEdit(account: Account) {
		draft = { ...account, apiKey: '' };
		albumsText = account.albums.join('\n');
		excludedAlbumsText = account.excludedAlbums.join('\n');
		peopleText = account.people.join('\n');
		tagsText = account.tags.join('\n');
		isNew = false;
		saveError = '';
		browse = null;
		browseError = '';
		editing = account;
	}

	function cancel() {
		editing = null;
	}

	function lines(text: string): string[] {
		return text
			.split('\n')
			.map((s) => s.trim())
			.filter((s) => s.length > 0);
	}

	function nullableDate(value: unknown): string | null {
		return value ? String(value) : null;
	}

	async function loadFromServer() {
		browseError = '';
		if (!draft.immichServerUrl) {
			browseError = 'Enter the server URL first.';
			return;
		}
		browsing = true;
		try {
			browse = await browseAccount({
				immichServerUrl: draft.immichServerUrl as string,
				apiKey: draft.apiKey ? (draft.apiKey as string) : undefined,
				accountId: isNew ? undefined : (editing as Account).id
			});
		} catch (e) {
			browse = null;
			browseError = e instanceof Error ? e.message : 'Could not reach the server.';
		} finally {
			browsing = false;
		}
	}

	function isChecked(text: string, id: string): boolean {
		return lines(text).includes(id);
	}

	function toggleId(text: string, id: string, checked: boolean): string {
		const set = new Set(lines(text));
		if (checked) set.add(id);
		else set.delete(id);
		return [...set].join('\n');
	}

	async function save() {
		saving = true;
		saveError = '';
		try {
			const payload: Partial<Account> = {
				...draft,
				imagesFromDate: nullableDate(draft.imagesFromDate),
				imagesUntilDate: nullableDate(draft.imagesUntilDate),
				albums: lines(albumsText),
				excludedAlbums: lines(excludedAlbumsText),
				people: lines(peopleText),
				tags: lines(tagsText)
			};
			const result = isNew
				? await createAccount(payload)
				: await updateAccount((editing as Account).id, payload);
			saveWarnings = result.warnings ?? [];
			editing = null;
			await load();
		} catch (e) {
			saveError = e instanceof Error ? e.message : 'Failed to save account.';
		} finally {
			saving = false;
		}
	}

	async function remove(account: Account) {
		if (!confirm(`Remove account ${account.immichServerUrl}?`)) return;
		saveWarnings = [];
		try {
			await deleteAccount(account.id);
			await load();
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to delete account.';
		}
	}

	function summary(a: Account): string {
		const flags = [
			a.showFavorites && 'favorites',
			a.showMemories && 'memories',
			a.showArchived && 'archived',
			a.showVideos && 'videos'
		].filter(Boolean);
		const counts = [
			a.albums.length && `${a.albums.length} album(s)`,
			a.people.length && `${a.people.length} people`,
			a.tags.length && `${a.tags.length} tag(s)`
		].filter(Boolean);
		return [...flags, ...counts].join(' · ') || 'all assets';
	}
</script>

{#if editing}
	<section class="rounded-xl border border-slate-700 bg-slate-800/60 p-5">
		<h2 class="mb-4 text-sm font-semibold uppercase tracking-wide text-slate-400">
			{isNew ? 'Add account' : 'Edit account'}
		</h2>
		<div class="space-y-4">
			<label class="block text-sm">
				<span class="mb-1 block text-slate-300">Immich server URL</span>
				<input bind:value={draft.immichServerUrl} placeholder="https://immich.example.com" class={inputClass} />
			</label>
			<label class="block text-sm">
				<span class="mb-1 block text-slate-300">API key {isNew ? '' : '(leave blank to keep current)'}</span>
				<input type="password" bind:value={draft.apiKey} autocomplete="off" class={inputClass} />
			</label>

			<div>
				<button type="button" onclick={loadFromServer} disabled={browsing} class="rounded-md border border-slate-600 px-3 py-1.5 text-sm hover:bg-slate-700 disabled:opacity-50">
					{browsing ? 'Loading…' : 'Load albums & people from server'}
				</button>
				{#if browseError}<span class="ml-3 text-sm text-red-400">{browseError}</span>{/if}
				{#if browse && browse.warnings.length}
					<ul class="mt-2 space-y-1 text-xs text-amber-300">
						{#each browse.warnings as w}<li>⚠ {w}</li>{/each}
					</ul>
				{/if}
			</div>

			<div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showFavorites} class="h-4 w-4" /> Favorites</label>
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showMemories} class="h-4 w-4" /> Memories</label>
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showArchived} class="h-4 w-4" /> Archived</label>
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showVideos} class="h-4 w-4" /> Videos</label>
			</div>

			<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">Images from last N days</span>
					<input type="number" bind:value={draft.imagesFromDays} class={inputClass} />
				</label>
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">From date</span>
					<input type="date" bind:value={draft.imagesFromDate} class={inputClass} />
				</label>
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">Until date</span>
					<input type="date" bind:value={draft.imagesUntilDate} class={inputClass} />
				</label>
			</div>

			<div class="space-y-4">
				<div>
					<span class="mb-1 block text-sm text-slate-300">Albums</span>
					{#if browse}
						<div class="mb-2 max-h-40 overflow-y-auto rounded-md border border-slate-600 bg-slate-700/40 p-2">
							{#each browse.albums as a (a.id)}
								<label class="flex items-center gap-2 py-0.5 text-sm">
									<input type="checkbox" checked={isChecked(albumsText, a.id)} onchange={(e) => (albumsText = toggleId(albumsText, a.id, e.currentTarget.checked))} />
									<span class="truncate">{a.name}</span>
								</label>
							{:else}
								<p class="text-xs text-slate-500">No albums found.</p>
							{/each}
						</div>
					{/if}
					<textarea bind:value={albumsText} rows="2" placeholder="Album IDs, one per line" class="{inputClass} font-mono text-xs"></textarea>
				</div>

				<div>
					<span class="mb-1 block text-sm text-slate-300">People</span>
					{#if browse}
						<div class="mb-2 max-h-40 overflow-y-auto rounded-md border border-slate-600 bg-slate-700/40 p-2">
							{#each browse.people as p (p.id)}
								<label class="flex items-center gap-2 py-0.5 text-sm">
									<input type="checkbox" checked={isChecked(peopleText, p.id)} onchange={(e) => (peopleText = toggleId(peopleText, p.id, e.currentTarget.checked))} />
									<span class="truncate">{p.name}</span>
								</label>
							{:else}
								<p class="text-xs text-slate-500">No named people found.</p>
							{/each}
						</div>
					{/if}
					<textarea bind:value={peopleText} rows="2" placeholder="Person IDs, one per line" class="{inputClass} font-mono text-xs"></textarea>
				</div>

				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<label class="block text-sm">
						<span class="mb-1 block text-slate-300">Excluded album IDs (one per line)</span>
						<textarea bind:value={excludedAlbumsText} rows="2" class="{inputClass} font-mono text-xs"></textarea>
					</label>
					<label class="block text-sm">
						<span class="mb-1 block text-slate-300">Tags (one per line)</span>
						<textarea bind:value={tagsText} rows="2" class="{inputClass} font-mono text-xs"></textarea>
					</label>
				</div>
			</div>

			<label class="block text-sm sm:w-40">
				<span class="mb-1 block text-slate-300">Minimum rating</span>
				<input type="number" min="0" max="5" bind:value={draft.rating} class={inputClass} />
			</label>

			{#if saveError}
				<p class="text-sm text-red-400">{saveError}</p>
			{/if}

			<div class="flex gap-3">
				<button onclick={save} disabled={saving} class="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium hover:bg-indigo-500 disabled:opacity-50">
					{saving ? 'Saving…' : 'Save account'}
				</button>
				<button onclick={cancel} class="rounded-md border border-slate-600 px-4 py-2 text-sm hover:bg-slate-800">Cancel</button>
			</div>
		</div>
	</section>
{:else}
	<div class="space-y-4">
		<div class="flex items-center justify-between">
			<p class="text-sm text-slate-400">Immich accounts the slideshow pulls from. Changes apply live.</p>
			<button onclick={startAdd} class="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium hover:bg-indigo-500">Add account</button>
		</div>

		{#if saveWarnings.length}
			<div class="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-amber-200">
				<p class="font-medium">Saved — but the API key may be missing permissions:</p>
				<ul class="mt-1 list-disc space-y-0.5 pl-5">
					{#each saveWarnings as w}<li>{w}</li>{/each}
				</ul>
			</div>
		{/if}

		{#if error}
			<p class="rounded-md border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-300">{error}</p>
		{/if}

		{#if loading}
			<p class="text-slate-400">Loading…</p>
		{:else if accounts.length === 0}
			<div class="rounded-xl border border-dashed border-slate-700 p-8 text-center text-slate-400">
				No accounts yet. Add one to start showing photos.
			</div>
		{:else}
			{#each accounts as account (account.id)}
				<div class="flex items-center justify-between rounded-xl border border-slate-700 bg-slate-800/60 p-4">
					<div class="min-w-0">
						<p class="truncate font-medium">{account.immichServerUrl}</p>
						<p class="mt-0.5 text-xs text-slate-400">{summary(account)}</p>
						<p class="mt-0.5 text-xs {account.hasApiKey ? 'text-green-400' : 'text-amber-400'}">
							{account.hasApiKey ? 'API key set' : 'No API key'}
						</p>
					</div>
					<div class="flex shrink-0 gap-2">
						<button onclick={() => startEdit(account)} class="rounded-md border border-slate-600 px-3 py-1.5 text-sm hover:bg-slate-700">Edit</button>
						<button onclick={() => remove(account)} class="rounded-md border border-red-500/40 px-3 py-1.5 text-sm text-red-300 hover:bg-red-500/10">Delete</button>
					</div>
				</div>
			{/each}
		{/if}
	</div>
{/if}
